using Meta.XR.MRUtilityKit;
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public class PrefabSpawnConfiguration
{
    [Tooltip("List of prefabs to choose from for this configuration")]
    public List<GameObject> Prefabs;

    [Tooltip("Minimum number of prefabs to spawn for this configuration")]
    public int MinAmount = 0;

    [Tooltip("Maximum number of prefabs to spawn for this configuration")]
    public int MaxAmount = 10;

    [Tooltip("Safety offset from surroundings (room boundaries and other objects) for this configuration")]
    public float Offset = 0.2f;
}

public class RoomPrefabSpawner : MonoBehaviour
{
    [SerializeField]
    public List<PrefabSpawnConfiguration> SpawnConfigurations;

    [SerializeField, Tooltip("Maximum attempts to find a safe spawn position per prefab")]
    public int MaxSpawnAttemptsPerPrefab = 20;

    [SerializeField]
    private bool DrawDebugBounds = false;

    private List<GameObject> spawnedObjects = new List<GameObject>();
    private MRUKRoom currentRoom;
    private List<Transform> roomBoundaries = new List<Transform>();
    private float offset = 0.1f;

    void Start()
    {
        if (MRUK.Instance)
        {
            MRUK.Instance.RegisterSceneLoadedCallback(() =>
            {
                currentRoom = MRUK.Instance.GetCurrentRoom();
                if (currentRoom != null)
                {
                    CollectRoomBoundaries();
                    SpawnPrefabs();
                }
                else
                {
                    Debug.LogError("MRUK Room is null. Cannot spawn prefabs.");
                }
            });
        }
    }

    private void CollectRoomBoundaries()
    {
        // Add wall, floor and ceiling anchors as boundaries
        foreach (var wall in currentRoom.WallAnchors)
        {
            if (wall != null && wall.transform != null)
                roomBoundaries.Add(wall.transform);
        }
        
        if (currentRoom.FloorAnchor != null && currentRoom.FloorAnchor.transform != null)
            roomBoundaries.Add(currentRoom.FloorAnchor.transform);
            
        if (currentRoom.CeilingAnchor != null && currentRoom.CeilingAnchor.transform != null)
            roomBoundaries.Add(currentRoom.CeilingAnchor.transform);
            
        Debug.Log($"Found {roomBoundaries.Count} room boundaries");
    }

    // Add a context menu item to regenerate prefabs
    [ContextMenu("Reset and Regenerate Prefabs")]
    public void ResetAndRegeneratePrefabs()
    {
        Debug.Log("Manually resetting and regenerating prefabs");
        
        // Clear existing objects
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
                DestroyImmediate(obj);
        }
        spawnedObjects.Clear();
        
        // Regenerate prefabs
        if (currentRoom != null)
        {
            SpawnPrefabs();
        }
        else
        {
            Debug.LogWarning("Cannot spawn prefabs - no MRUK room available. Move the headset around to detect the room first.");
        }
    }
    
    public void SpawnPrefabs()
    {
        // Clear any previously spawned objects
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawnedObjects.Clear();
        
        Bounds roomBounds = currentRoom.GetRoomBounds();
        Debug.Log($"Room bounds: {roomBounds.min} to {roomBounds.max}, size: {roomBounds.size}");
        
        foreach (var config in SpawnConfigurations)
        {
            if (config.Prefabs == null || config.Prefabs.Count == 0)
            {
                Debug.LogWarning("Configuration has no prefabs. Skipping.");
                continue;
            }
            
            // Calculate reasonable maximum based on room size
            float roomArea = roomBounds.size.x * roomBounds.size.z; // Floor area
            float avgPrefabSize = 0.5f; // Estimate for average prefab radius
            float prefabArea = avgPrefabSize * avgPrefabSize * 4; // Rough area including spacing
            
            // Theoretical max objects that could fit in the room with spacing
            int theoreticalMax = Mathf.FloorToInt(roomArea / prefabArea);
            
            // Limit maximum to a reasonable number and not more than theoretical max
            int adjustedMax = Mathf.Min(config.MaxAmount, theoreticalMax, 100); 
            
            int toSpawn = Random.Range(Mathf.Min(config.MinAmount, adjustedMax), adjustedMax + 1);
            int spawned = 0;
            int totalAttempts = 0;
            int maxTotalAttempts = toSpawn * MaxSpawnAttemptsPerPrefab * 2; // Overall limit
            
            Debug.Log($"Attempting to spawn {toSpawn} prefabs (adjusted from max {config.MaxAmount} to {adjustedMax}) with offset {config.Offset}");
            
            // Try to spawn prefabs
            while (spawned < toSpawn && totalAttempts < maxTotalAttempts)
            {
                if (SimpleSpawnPrefab(config, roomBounds))
                {
                    spawned++;
                }
                totalAttempts++;
                
                // If we're getting close to the attempt limit, stop to avoid performance issues
                if (totalAttempts > maxTotalAttempts * 0.8f)
                {
                    Debug.LogWarning($"Approaching maximum attempts ({totalAttempts}/{maxTotalAttempts}). Stopping spawning.");
                    break;
                }
            }
            
            Debug.Log($"Spawned {spawned}/{toSpawn} prefabs for configuration after {totalAttempts} attempts");
        }
    }
    
    // Simplified prefab spawning method with more direct control
    private bool SimpleSpawnPrefab(PrefabSpawnConfiguration config, Bounds roomBounds)
    {
        GameObject prefab = GetRandomPrefab(config.Prefabs);
        if (prefab == null)
            return false;
        
        float offset = config.Offset;
        
        // Get prefab size
        Bounds prefabBounds = EstimatePrefabBounds(prefab);
        
        // Create valid spawn area (shrunken by offset + prefab extents)
        float minX = roomBounds.min.x + prefabBounds.extents.x + offset;
        float maxX = roomBounds.max.x - prefabBounds.extents.x - offset;
        float minZ = roomBounds.min.z + prefabBounds.extents.z + offset;
        float maxZ = roomBounds.max.z - prefabBounds.extents.z - offset;
        
        // Check if the room is too small for this prefab
        if (minX >= maxX || minZ >= maxZ)
        {
            Debug.LogWarning($"Room is too small for prefab {prefab.name} with offset {offset}");
            return false;
        }
        
        float floorY = roomBounds.min.y;
        
        // Try several random positions
        for (int attempt = 0; attempt < MaxSpawnAttemptsPerPrefab; attempt++)
        {
            // Generate a random position within the valid spawn area
            Vector3 position = new Vector3(
                Random.Range(minX, maxX),
                0, // Y will be set after spawning
                Random.Range(minZ, maxZ)
            );
            
            // Check for collisions with existing objects
            bool hasCollision = false;
            foreach (var obj in spawnedObjects)
            {
                if (obj == null) continue;
                
                // Get the actual bounds of the existing object
                Bounds existingObjBounds = GetActualBounds(obj);
                
                // Calculate minimum distance based on both objects' sizes
                // Use the maximum of width and depth for each object to ensure proper spacing
                float newObjSize = Mathf.Max(prefabBounds.size.x, prefabBounds.size.z);
                float existingObjSize = Mathf.Max(existingObjBounds.size.x, existingObjBounds.size.z);
                
                // Minimum required separation is half the size of each object plus the offset
                float minRequiredDistance = (newObjSize + existingObjSize) * 0.5f + offset;
                
                // Check horizontal distance (XZ plane)
                Vector2 pos2D = new Vector2(position.x, position.z);
                Vector2 objPos2D = new Vector2(obj.transform.position.x, obj.transform.position.z);
                float actualDistance = Vector2.Distance(pos2D, objPos2D);
                
                if (actualDistance < minRequiredDistance)
                {
                    hasCollision = true;
                    //Debug.Log($"Collision detected: Distance between objects ({actualDistance}) is less than required ({minRequiredDistance})");
                    break;
                }
            }
            
            if (hasCollision)
                continue;
            
            // Position is valid, spawn the prefab
            GameObject spawnedObject = Instantiate(prefab, position, Quaternion.Euler(0, Random.Range(0, 360), 0), transform);
            
            // Adjust Y position to place on floor
            Bounds actualBounds = GetActualBounds(spawnedObject);
            
            // Calculate how far the bottom of the object is from its pivot point
            float pivotToBottomOffset = spawnedObject.transform.position.y - actualBounds.min.y;
            
            // Move the object so the bottom sits exactly on the floor
            Vector3 finalPosition = spawnedObject.transform.position;
            finalPosition.y = floorY + pivotToBottomOffset;
            spawnedObject.transform.position = finalPosition;
            
            // Verify the object is within bounds after Y adjustment
            actualBounds = GetActualBounds(spawnedObject);
            
            // Final boundary check
            if (actualBounds.min.x < roomBounds.min.x + offset || 
                actualBounds.max.x > roomBounds.max.x - offset ||
                actualBounds.min.z < roomBounds.min.z + offset || 
                actualBounds.max.z > roomBounds.max.z - offset)
            {
                Debug.LogWarning($"Prefab {prefab.name} is outside offset boundaries after placement. Destroying.");
                Destroy(spawnedObject);
                continue;
            }
            
            // Final collision check with all existing objects
            bool finalCollision = false;
            foreach (var obj in spawnedObjects)
            {
                if (obj == null) continue;
                
                Bounds existingObjBounds = GetActualBounds(obj);
                Bounds newObjBounds = actualBounds;
                
                // Expand bounds slightly for the check
                Bounds expandedExistingBounds = new Bounds(
                    existingObjBounds.center,
                    existingObjBounds.size + new Vector3(offset * 2, 0, offset * 2)
                );
                
                if (expandedExistingBounds.Intersects(newObjBounds))
                {
                    finalCollision = true;
                    Debug.LogWarning($"Prefab {prefab.name} intersects with existing object after placement. Destroying.");
                    break;
                }
            }
            
            if (finalCollision)
            {
                Destroy(spawnedObject);
                continue;
            }
            
            spawnedObjects.Add(spawnedObject);
            return true;
        }
        
        return false;
    }
    
    // Get actual bounds of an instantiated object
    private Bounds GetActualBounds(GameObject obj)
    {
        Bounds bounds = new Bounds(obj.transform.position, Vector3.one * 0.1f);
        bool initialized = false;
        
        // Check renderers
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        
        // If no renderers, check colliders
        if (!initialized)
        {
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
            {
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }
        
        // If still no bounds, use transform-based bounds
        if (!initialized)
        {
            bounds = new Bounds(obj.transform.position, Vector3.one * 0.3f);
        }
        
        return bounds;
    }
    
    // Estimate bounds of a prefab
    private Bounds EstimatePrefabBounds(GameObject prefab)
    {
        // Create a temporary instance to get accurate bounds
        GameObject tempInstance = Instantiate(prefab, new Vector3(0, -1000, 0), Quaternion.identity); // Place far below to avoid interference
        tempInstance.SetActive(true);
        
        Bounds bounds = GetActualBounds(tempInstance);
        
        // Clean up
        Destroy(tempInstance);
        
        return bounds;
    }
    
    private bool TrySpawnPrefab(PrefabSpawnConfiguration config, Bounds roomBounds, float overrideOffset = -1f)
    {
        float offset = overrideOffset >= 0 ? overrideOffset : config.Offset;
        GameObject prefab = GetRandomPrefab(config.Prefabs);
        
        if (prefab == null)
            return false;
        
        Debug.Log($"<<<< ATTEMPTING TO SPAWN PREFAB: {prefab.name} WITH OFFSET {offset} >>>>");
        
        // Try up to 3 methods of spawning with decreasing constraints
        for (int method = 0; method < 3; method++) // Adding back method 2 (forced placement) for troubleshooting
        {
            Bounds prefabBounds = CalculatePrefabBounds(prefab);
            if (prefabBounds.size == Vector3.zero)
            {
                Debug.LogWarning($"Prefab {prefab.name} has zero bounds. Using minimum bounds.");
                prefabBounds = new Bounds(Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f));
            }
            
            Debug.Log($"Attempting to spawn prefab {prefab.name} with bounds size {prefabBounds.size} (Method: {method})");
            
            // Use progressively smaller offsets for more aggressive placement attempts
            float currentOffset = method == 0 ? offset : (method == 1 ? offset * 0.5f : 0.01f);
            Debug.Log($"Using offset: {currentOffset} for attempt method {method}");
            
            // Store the current offset for use in SpawnPrefabAtPosition
            this.offset = currentOffset;
            
            // Increase the effective extents of the prefab for safer placement
            float safetyFactor = 1.1f; // Add 10% to the prefab size for safer positioning
            Bounds safePrefabBounds = new Bounds(
                prefabBounds.center,
                new Vector3(
                    prefabBounds.size.x * safetyFactor,
                    prefabBounds.size.y,
                    prefabBounds.size.z * safetyFactor
                )
            );
            
            // Get interior bounds (room bounds shrunk by offset - now only in XZ plane)
            Bounds interiorBounds = new Bounds(
                roomBounds.center, 
                new Vector3(
                    roomBounds.size.x - currentOffset * 2,  // Apply offset in X
                    roomBounds.size.y,                      // No offset in Y
                    roomBounds.size.z - currentOffset * 2   // Apply offset in Z
                )
            );
            
            // Check if the interior bounds are too small - if so, try with smaller offset
            if (interiorBounds.size.x <= safePrefabBounds.size.x || 
                interiorBounds.size.z <= safePrefabBounds.size.z)
            {
                if (method < 2) {
                    Debug.LogWarning($"Room too small for method {method}, trying with reduced constraints");
                    continue; // Try next method with reduced constraints
                } else {
                    Debug.LogError($"Room is too small for prefab even with minimal constraints.");
                    return false;
                }
            }
            
            // Find the floor y-coordinate (minimum y of room bounds)
            float floorY = roomBounds.min.y;
            
            Debug.Log($"Room bounds: {roomBounds.min} to {roomBounds.max}, Floor Y: {floorY}");
            Debug.Log($"Interior bounds: {interiorBounds.min} to {interiorBounds.max}");
            
            bool extraLogging = true; // Enable logging to troubleshoot issues
            
            // Start with simple random positioning for first few attempts
            // Use larger increment between attempts when prefab boundaries are a problem
            float increment = 0.2f; // Initial offset from edges
            float maxIncrement = Math.Min(interiorBounds.size.x, interiorBounds.size.z) * 0.4f; // Up to 40% of room width
            
            for (int attempt = 0; attempt < 10; attempt++) 
            {
                // For later attempts, move further from the edges
                float edgeOffset = Math.Min(increment * attempt, maxIncrement);
                
                // Shrink available area for safer placement in later attempts
                Vector3 position = new Vector3(
                    Random.Range(
                        interiorBounds.min.x + safePrefabBounds.extents.x + edgeOffset, 
                        interiorBounds.max.x - safePrefabBounds.extents.x - edgeOffset
                    ),
                    0,
                    Random.Range(
                        interiorBounds.min.z + safePrefabBounds.extents.z + edgeOffset, 
                        interiorBounds.max.z - safePrefabBounds.extents.z - edgeOffset
                    )
                );
                
                if (extraLogging)
                    Debug.Log($"SIMPLE - Attempt {attempt+1}: Generated position {position} for {prefab.name} (edge offset: {edgeOffset})");
                
                bool positionClear = method == 2 ? true : CheckPosition(position, safePrefabBounds, currentOffset, extraLogging);
                
                if (positionClear || method == 2)
                {
                    bool succeeded = SpawnPrefabAtPosition(prefab, position, floorY, extraLogging);
                    if (succeeded)
                        return true;
                }
            }
            
            Debug.Log("Simple positioning failed, trying room center");
            
            // Try room center as a fallback
            Vector3 centerPosition = new Vector3(
                roomBounds.center.x,
                0,
                roomBounds.center.z
            );
            
            bool centerClear = method == 2 ? true : CheckPosition(centerPosition, safePrefabBounds, currentOffset, extraLogging);
            if (centerClear || method == 2)
            {
                bool succeeded = SpawnPrefabAtPosition(prefab, centerPosition, floorY, extraLogging);
                if (succeeded)
                    return true;
            }
            
            Debug.LogWarning($"Method {method} - Failed to find position");
        }
        
        Debug.LogWarning($"Failed to spawn prefab {prefab.name} after all methods");
        return false;
    }
    
    // Extracted method to check if a position is valid
    private bool CheckPosition(Vector3 position, Bounds prefabBounds, float offset, bool logDetails)
    {
        if (logDetails)
            Debug.Log($"Checking position: {position}");
            
        foreach (var obj in spawnedObjects)
        {
            if (obj == null) continue;
            
            // Only check XZ distance (horizontal plane)
            Vector2 pos2D = new Vector2(position.x, position.z);
            Vector2 objPos2D = new Vector2(obj.transform.position.x, obj.transform.position.z);
            
            // Calculate 2D radius (horizontal extents) - use the larger of X and Z
            float objRadius = Mathf.Max(prefabBounds.extents.x, prefabBounds.extents.z);
            
            // Minimum horizontal distance between objects
            float minDistance = objRadius + offset;
            float actualDistance = Vector2.Distance(pos2D, objPos2D);
            
            if (actualDistance < minDistance)
            {
                if (logDetails)
                    Debug.Log($"Position too close to existing object {obj.name} (distance: {actualDistance}, min required: {minDistance})");
                return false;
            }
        }
        
        // Check room boundaries - only in XZ plane, but be very strict about it
        Bounds roomBounds = currentRoom.GetRoomBounds();
        
        // Calculate the horizontal extents of the prefab
        float prefabExtentX = prefabBounds.extents.x;
        float prefabExtentZ = prefabBounds.extents.z;
        
        // Calculate the safe area where the prefab can be placed (including offset)
        float safeMinX = roomBounds.min.x + prefabExtentX + offset;
        float safeMaxX = roomBounds.max.x - prefabExtentX - offset;
        float safeMinZ = roomBounds.min.z + prefabExtentZ + offset;
        float safeMaxZ = roomBounds.max.z - prefabExtentZ - offset;
        
        // Check if the position is within the safe area
        if (position.x < safeMinX || position.x > safeMaxX || 
            position.z < safeMinZ || position.z > safeMaxZ)
        {
            if (logDetails)
                Debug.Log($"Position outside safe boundaries: Position:{position}, " +
                         $"Safe X:[{safeMinX}-{safeMaxX}], Safe Z:[{safeMinZ}-{safeMaxZ}]");
            return false;
        }
        
        if (logDetails)
            Debug.Log($"Position is valid");
        return true;
    }
    
    // Extracted method to spawn a prefab at a specific position
    private bool SpawnPrefabAtPosition(GameObject prefab, Vector3 position, float floorY, bool logDetails)
    {
        try
        {
            if (logDetails)
                Debug.Log($"Spawning {prefab.name} at position {position}");
            
            // Get current offset being used
            float currentOffset = offset; // Store the current method's offset
            
            // Create a prefab with Y=0 first (for positioning)
            Vector3 initialPosition = new Vector3(position.x, 0, position.z);
            GameObject spawnedObject = Instantiate(prefab, initialPosition, Quaternion.Euler(0, Random.Range(0, 360), 0), transform);
            
            // Get exact bounds after instantiation
            Bounds actualBounds = GetQuickBounds(spawnedObject);
            
            // Double-check boundaries to ensure the prefab is fully within the room
            Bounds roomBounds = currentRoom.GetRoomBounds();
            
            // First check if it's within the actual room boundaries
            if (!IsPrefabFullyWithinBoundaries(spawnedObject, actualBounds, roomBounds))
            {
                if (logDetails)
                    Debug.LogWarning($"Prefab would be outside room boundaries after spawning. Destroying and retrying.");
                Destroy(spawnedObject);
                return false;
            }
            
            // Then check if it's within the offset boundaries (stricter check)
            if (!IsPrefabWithinOffsetBoundaries(spawnedObject, actualBounds, roomBounds, currentOffset))
            {
                if (logDetails)
                    Debug.LogWarning($"Prefab would be outside offset boundaries after spawning. Destroying and retrying.");
                Destroy(spawnedObject);
                return false;
            }
            
            // Adjust Y position after spawn
            float bottomY = actualBounds.min.y;
            float pivotToBottomOffset = spawnedObject.transform.position.y - bottomY;
            
            // Calculate Y position to place on floor
            float correctY = floorY + pivotToBottomOffset;
            
            if (logDetails)
            {
                Debug.Log($"Adjusting Y position: current={spawnedObject.transform.position.y}, " +
                         $"corrected={correctY}, offset={pivotToBottomOffset}");
            }
            
            // Apply corrected position
            Vector3 finalPosition = spawnedObject.transform.position;
            finalPosition.y = correctY;
            spawnedObject.transform.position = finalPosition;
            
            // Final check after Y adjustment
            actualBounds = GetQuickBounds(spawnedObject);
            
            // Check room boundaries again
            if (!IsPrefabFullyWithinBoundaries(spawnedObject, actualBounds, roomBounds))
            {
                if (logDetails)
                    Debug.LogWarning($"Prefab outside room boundaries after Y adjustment. Destroying.");
                Destroy(spawnedObject);
                return false;
            }
            
            // Check offset boundaries again
            if (!IsPrefabWithinOffsetBoundaries(spawnedObject, actualBounds, roomBounds, currentOffset))
            {
                if (logDetails)
                    Debug.LogWarning($"Prefab outside offset boundaries after Y adjustment. Destroying.");
                Destroy(spawnedObject);
                return false;
            }
            
            spawnedObjects.Add(spawnedObject);
            
            Debug.Log($"SUCCESS: Spawned {prefab.name} at position {finalPosition}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error spawning prefab: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }
    
    // Additional helper method to ensure prefab is fully within boundaries
    private bool IsPrefabFullyWithinBoundaries(GameObject prefab, Bounds prefabBounds, Bounds roomBounds)
    {
        // Check if the prefab is fully within the room boundaries
        if (prefabBounds.min.x < roomBounds.min.x ||
            prefabBounds.min.z < roomBounds.min.z ||
            prefabBounds.max.x > roomBounds.max.x ||
            prefabBounds.max.z > roomBounds.max.z)
        {
            // Log detailed information about the boundary violation
            string message = $"Prefab {prefab.name} is outside boundaries:\n" +
                           $"Prefab bounds: {prefabBounds.min} to {prefabBounds.max}\n" +
                           $"Room bounds: {roomBounds.min} to {roomBounds.max}\n";
                           
            if (prefabBounds.min.x < roomBounds.min.x)
                message += $"Left side out by: {roomBounds.min.x - prefabBounds.min.x}m\n";
            if (prefabBounds.max.x > roomBounds.max.x)
                message += $"Right side out by: {prefabBounds.max.x - roomBounds.max.x}m\n";
            if (prefabBounds.min.z < roomBounds.min.z)
                message += $"Back side out by: {roomBounds.min.z - prefabBounds.min.z}m\n";
            if (prefabBounds.max.z > roomBounds.max.z)
                message += $"Front side out by: {prefabBounds.max.z - roomBounds.max.z}m\n";
                
            Debug.LogWarning(message);
            return false;
        }
        return true;
    }
    
    // New method to check if a prefab is within the offset boundaries
    private bool IsPrefabWithinOffsetBoundaries(GameObject prefab, Bounds prefabBounds, Bounds roomBounds, float offset)
    {
        // Create inset bounds by shrinking the room boundaries by the offset
        Bounds offsetBounds = new Bounds(
            roomBounds.center,
            new Vector3(
                roomBounds.size.x - (offset * 2),
                roomBounds.size.y,
                roomBounds.size.z - (offset * 2)
            )
        );
        
        // Check if any part of the prefab exceeds the offset boundaries
        if (prefabBounds.min.x < offsetBounds.min.x ||
            prefabBounds.min.z < offsetBounds.min.z ||
            prefabBounds.max.x > offsetBounds.max.x ||
            prefabBounds.max.z > offsetBounds.max.z)
        {
            // Log detailed information about the offset violation
            string message = $"Prefab {prefab.name} is outside offset boundaries:\n" +
                           $"Prefab bounds: {prefabBounds.min} to {prefabBounds.max}\n" +
                           $"Offset bounds: {offsetBounds.min} to {offsetBounds.max} (offset: {offset}m)\n";
                           
            if (prefabBounds.min.x < offsetBounds.min.x)
                message += $"Left side inside offset by: {offsetBounds.min.x - prefabBounds.min.x}m\n";
            if (prefabBounds.max.x > offsetBounds.max.x)
                message += $"Right side inside offset by: {prefabBounds.max.x - offsetBounds.max.x}m\n";
            if (prefabBounds.min.z < offsetBounds.min.z)
                message += $"Back side inside offset by: {offsetBounds.min.z - prefabBounds.min.z}m\n";
            if (prefabBounds.max.z > offsetBounds.max.z)
                message += $"Front side inside offset by: {prefabBounds.max.z - offsetBounds.max.z}m\n";
                
            Debug.LogWarning(message);
            return false;
        }
        return true;
    }
    
    // Quick method to get bounds (simplified)
    private Bounds GetQuickBounds(GameObject obj)
    {
        Bounds bounds = new Bounds(obj.transform.position, Vector3.one * 0.3f);
        bool initialized = false;
        
        // Check renderers first
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        
        // If no renderers, check colliders
        if (!initialized)
        {
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
            {
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }
        
        // If still not initialized, use a default size
        if (!initialized)
        {
            bounds = new Bounds(obj.transform.position, Vector3.one * 0.3f);
        }
        
        return bounds;
    }
    
    private Bounds CalculatePrefabBounds(GameObject prefab)
    {
        // For prefabs in the scene, we need to use a different approach than for prefabs in the project
        bool isPrefabInstance = prefab.scene.name != null;
        
        if (isPrefabInstance)
        {
            // This is an instantiated object, so we can directly access its bounds
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;
            
            // Check renderers
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            
            // Check colliders
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
            {
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            
            // If no renderers or colliders found, use a fallback approach
            if (!initialized)
            {
                // Use transform-based bounds
                bounds = GetTransformBasedBounds(prefab.transform);
            }
            
            return bounds;
        }
        else
        {
            // For a prefab asset, we need to create a temporary instance to get accurate bounds
            GameObject tempInstance = Instantiate(prefab);
            // Make sure it's active so we can get accurate bounds
            tempInstance.SetActive(true);
            
            // Get bounds
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;
            
            Renderer[] renderers = tempInstance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            
            Collider[] colliders = tempInstance.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            
            // If no renderers or colliders found, use a fallback approach
            if (!initialized)
            {
                // Use transform-based bounds
                bounds = GetTransformBasedBounds(tempInstance.transform);
            }
            
            // If still uninitialized, use a minimum default size
            if (!initialized || bounds.size == Vector3.zero)
            {
                // Use a minimum default size to ensure objects can spawn
                float minSize = 0.3f; // Small default size
                bounds = new Bounds(Vector3.zero, new Vector3(minSize, minSize, minSize));
            }
            
            // Debug the bounds
            Debug.Log($"Calculated bounds for prefab {prefab.name}: center={bounds.center}, size={bounds.size}");
            
            // Cleanup
            Destroy(tempInstance);
            
            return bounds;
        }
    }
    
    // Fallback method to calculate bounds based on transforms
    private Bounds GetTransformBasedBounds(Transform root)
    {
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        bool initialized = false;
        
        // Include all child transforms in the bounds
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            if (!initialized)
            {
                bounds = new Bounds(child.position, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(child.position);
            }
        }
        
        // Ensure the bounds have some minimum size
        if (bounds.size.magnitude < 0.1f)
        {
            // Add small extents to ensure non-zero size bounds
            bounds.Expand(0.3f);
        }
        
        return bounds;
    }
    
    private GameObject GetRandomPrefab(List<GameObject> prefabs)
    {
        if (prefabs == null || prefabs.Count == 0)
            return null;
            
        return prefabs[Random.Range(0, prefabs.Count)];
    }
    
    void OnDrawGizmosSelected()
    {
        if (DrawDebugBounds && currentRoom != null)
        {
            Bounds roomBounds = currentRoom.GetRoomBounds();
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(roomBounds.center, roomBounds.size);
            
            // Draw interior bounds for the first configuration if available
            if (SpawnConfigurations != null && SpawnConfigurations.Count > 0)
            {
                float offset = SpawnConfigurations[0].Offset;
                Bounds interiorBounds = new Bounds(
                    roomBounds.center, 
                    roomBounds.size - new Vector3(offset * 2, offset * 2, offset * 2)
                );
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(interiorBounds.center, interiorBounds.size);
            }
        }
    }
}