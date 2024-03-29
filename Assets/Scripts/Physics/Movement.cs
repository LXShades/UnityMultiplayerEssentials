﻿using System;
using System.Collections.Generic;
using UnityEngine;
using static Movement;

/// <summary>
/// Manually-controllable/tickable immediate sweep-based movement features for characters and objects
/// Similar to Unity's CharacterController, with more features and collision shapes.
/// </summary>
public class Movement : MonoBehaviour
{
    public struct Hit
    {
        /// <summary>The normal of the most significant hit</summary>
        public Vector3 normal;

        /// <summary>Collider hit during the most significant hit</summary>
        public Collider collider;

        /// <summary>Whether the point field is valid. Penetration collisions do not supply this</summary>
        public bool hasPoint;

        /// <summary>The point that was hit, if available. Check hasPoint first.</summary>
        public Vector3 point;
    }

    [Flags]
    public enum MoveFlags
    {
        /// <summary>
        /// Moves as far as it can. If a hit occurs on a flat surface, its trajectory is adjusted to move along the surface so that roughly the same distance is moved as originally intended
        /// </summary>
        None = 0,

        /// <summary>
        /// Moves as far as it can until it hits anything solid, and stops there.
        /// </summary>
        NoSlide = 1
    }

    public enum MoveType
    {
        /// <summary>
        /// Sweeps if the distance is far enough. Then uses a penetration test to remove the object from surfaces.
        /// </summary>
        SweepThenPenetration,

        /// <summary>
        /// Uses a sweep test for movement
        /// </summary>
        Sweep,

        /// <summary>
        /// Uses a penetration test for movement
        /// </summary>
        Penetration,
    }

    [Header("[Movement] Collision")]
    public bool enableCollision = true;

    [Header("Note: Colliders cannot currently rotate")]
    [Tooltip("List of colliders to be used in collision tests")]
    public Collider[] colliders = new Collider[0];

    [Tooltip("List of collision layers to interact with")]
    public LayerMask blockingCollisionLayers = ~0;

    [Tooltip("A list of objects (NOT object types) to ignore in collisions")]
    public HashSet<GameObject> gameObjectInstancesToIgnore = new HashSet<GameObject>();

    [Header("[Movement] Basic physics")]
    [Tooltip("If enabled, this component will control the object automatically on Update.")]
    public bool enableAutomaticPhysics = false;

    [Tooltip("Multiplier against gravity during basic physics simulation")]
    public float gravityMultiplier = 1f;

    [Tooltip("How much to bounce off objects during a basic physics simulation"), Range(0, 1)]
    public float bounceFactor = 0f;

    [Tooltip("When bouncing, this is a speed multiplier optionally reducing the speed where 0 does nothing and 1 cuts off all velocity"), Range(0, 1)]
    public float bounceFriction = 0f;

    [Header("[Movement] Advanced")]
    [Tooltip("Type of collision testing to use while moving, by default. Sweep is fast over long distances but loses accuracy with very small distances, and does not save an object if it has already passed through something. ")]
    public MoveType defaultMoveType = MoveType.SweepThenPenetration;

    [Tooltip("Distance we can move before triggering a sweep test. Ideal value depends on the phase of the moon and an Ouija board")]
    public float minSweepThresholdUntilPenetration = 0.05f;

    [Tooltip("Max number of collision steps to make. While the number of steps at maxCollisionStepSize varies depending on speed, this is an absolute maximum")]
    public int maxNumCollisionSteps = 3;

    [Tooltip("Size of a collision step before it is divided into another. Should be about half the size of the hitbox if using penetration testing.")]
    public float maxCollisionStepSize = 0.2f;

    [Tooltip("How much to pull back the object before doing a sweep test. A sweep test will, annoyingly, frequently rip straight through objects if it is started at the exact edge of the object. This tweak can prevent that. " +
        "The default setting is usually fine. Avoid using values too large as the object may pass through things behind it! Avoid using values too small or movements starting on the edge of objects will go straight through them!")]
    public float sweepPullback = 0.005f;

    [Tooltip("Sort of like pullback but on the opposite end. Tries to keep a slight buffer of space between self and collided objects. " +
        "This is needed because e.g. a depenetration collision may put you at the edge of an object before a sweep collision subsequently sends you through it.")]
    public float skinThickness = 0.025f;

    /// <summary>
    /// The current velocity of the object
    /// </summary>
    [HideInInspector] public Vector3 velocity;

    // hit buffers for specific functions, avoiding allocations
    private RaycastHit[] moveHitBuffer = new RaycastHit[128];
    private RaycastHit[] colliderCastHitBuffer = new RaycastHit[128];
    private static Hit[] genericHitBuffer = new Hit[12];
    private static Hit[] genericHitBufferB = new Hit[12];
    private static Hit[] genericSingleHitBuffer = new Hit[1];
    private Collider[] nearbyColliderBuffer = new Collider[24];
    private HashSet<IMovementCollisionCallbacks> movementCollisionCallbackBuffer = new HashSet<IMovementCollisionCallbacks>();
    private List<IMovementCollisionCallbacks> movementCollisionCallbackComponentsBuffer = new List<IMovementCollisionCallbacks>(12);

    private System.Diagnostics.Stopwatch methodStopwatch = new System.Diagnostics.Stopwatch();

    void Update()
    {
        if (enableAutomaticPhysics)
            SimulateBasicPhysics(Time.deltaTime, true);
    }

    /// <summary>
    /// Runs a default physics simulation (gravity, bounces, etc according to public parameters)
    /// </summary>
    public void SimulateBasicPhysics(float deltaTime, bool applyFinalMovement = true)
    {
        // Do the gravity
        velocity += Physics.gravity * deltaTime * gravityMultiplier;

        // Do the movement
        if (applyFinalMovement && Move(velocity * deltaTime, out Hit hit))
        {
            Vector3 resistanceVector = hit.normal * (-Vector3.Dot(hit.normal, velocity) * (1f + bounceFactor));
            Vector3 frictionVector = -velocity * bounceFriction;

            frictionVector -= hit.normal * Vector3.Dot(hit.normal, frictionVector);

            velocity += resistanceVector + frictionVector;
        }
    }

    /// <summary>
    /// Moves with collision checking depending on default move type. Can be a computationally expensive operation.
    /// </summary>
    public bool Move(Vector3 offset) => Move(offset, out Hit _);

    /// <summary>
    /// Moves with collision checking depending on default move type. Can be a computationally expensive operation.
    /// </summary>
    public bool Move(Vector3 offset, out Hit hitOut) => Move(offset, out hitOut, TickInfo.Default);

    /// <summary>
    /// Moves with collision checking depending on default move type. Can be a computationally expensive operation.
    /// </summary>
    public bool Move(Vector3 offset, out Hit hitOut, TickInfo tickInfo, MoveFlags flags = MoveFlags.None)
    {
        int ret = Move(offset, genericSingleHitBuffer, tickInfo);
        hitOut = ret > 0 ? genericSingleHitBuffer[0] : default;
        return ret > 0;
    }

    /// <summary>
    /// Moves with collision checking depending on default move type. Calls IMovementCollision callbacks on collided objects. Can be a computationally expensive operation. Returns whether a collision occurred.
    /// When using a Sweep collision, hitOut is based on the result of the sweep. With Penetration collisions, it is only estimated, based on where the object was pushed out.
    /// When using SweepThenPenetration, the sweep hit result has priority over the penetration result if there is one, as the former tend to be more accurate.
    /// </summary>
    public int Move(Vector3 offset, Hit[] hitsOut, TickInfo tickInfo, MoveFlags flags = MoveFlags.None)
    {
        if (defaultMoveType == MoveType.Sweep || flags != MoveFlags.None) // we don't suppose NoSlide on MovePenetration yet, todo?
        {
            return MoveSweep(offset, hitsOut, tickInfo, flags);
        }
        else if (defaultMoveType == MoveType.Penetration)
        {
            return MovePenetration(offset, hitsOut, tickInfo);
        }
        else
        {
            int numHitsA = MoveSweep(offset, genericHitBuffer, tickInfo, flags);
            int numHitsB = MovePenetration(Vector3.zero, genericHitBufferB, tickInfo);

            if (numHitsA > 0)
            {
                Array.Copy(genericHitBuffer, hitsOut, hitsOut.Length < numHitsA ? hitsOut.Length : numHitsA);
                return numHitsA;
            }
            else if (numHitsB > 0)
            {
                Array.Copy(genericHitBufferB, hitsOut, hitsOut.Length < numHitsB ? hitsOut.Length : numHitsB);
                return numHitsB;
            }
        }

        return 0;
    }

    /// <summary>
    /// Performs a sweep test-based movement
    /// </summary>
    public bool MoveSweep(Vector3 offset, out Hit hitOut, in TickInfo tickInfo, MoveFlags flags = MoveFlags.None)
    {
        int ret = MoveSweep(offset, genericSingleHitBuffer, tickInfo, flags);
        hitOut = ret > 0 ? genericSingleHitBuffer[0] : default;
        return ret > 0;
    }

    /// <summary>
    /// Performs a sweep test-based movement
    /// </summary>
    public int MoveSweep(Vector3 offset, Hit[] hitsOut, in TickInfo tickInfo, MoveFlags flags = MoveFlags.None)
    {
        if (offset.sqrMagnitude == 0f)
            return 0; // no moving to do

        if (!enableCollision)
        {
            transform.position += offset;
            return 0; // no collisions to do
        }

        methodStopwatch.Restart();

        int numIterations = (flags & MoveFlags.NoSlide) != 0 ? 1 : 3; // final iteration always blocks without slide, so just use one iteration
        Vector3 currentMovement = offset;
        int hitBufferSize = hitsOut != null ? hitsOut.Length : 0;
        int numHitsTotal = 0;

        movementCollisionCallbackBuffer.Clear();

        for (int iteration = 0; iteration < numIterations; iteration++)
        {
            RaycastHit hit;
            float currentMovementMagnitude = currentMovement.magnitude;
            float pullback = sweepPullback;
            Vector3 normalMovement = currentMovement.normalized;

            int numHits = ColliderCast(moveHitBuffer, transform.position, normalMovement, currentMovementMagnitude + skinThickness, blockingCollisionLayers, QueryTriggerInteraction.Collide, pullback);
            float closestDist = currentMovementMagnitude + skinThickness;
            int closestHitId = -1;

            // find closest blocking collider
            for (int i = 0; i < numHits; i++)
            {
                if (gameObjectInstancesToIgnore.Contains(moveHitBuffer[i].collider.gameObject))
                    continue;

                // acknowledge all collided movementcollision objects
                moveHitBuffer[i].collider.GetComponents<IMovementCollisionCallbacks>(movementCollisionCallbackComponentsBuffer);

                bool isTrigger = moveHitBuffer[i].collider.isTrigger;
                bool shouldBlock = !isTrigger && movementCollisionCallbackComponentsBuffer.Count == 0; // false if a trigger, and false if there are callbacks to evaluate first
                foreach (IMovementCollisionCallbacks movementCollision in movementCollisionCallbackComponentsBuffer)
                {
                    movementCollisionCallbackBuffer.Add(movementCollision);

                    if (!isTrigger)
                        shouldBlock |= movementCollision.ShouldBlockMovement(this, moveHitBuffer[i]);
                }

                // consider it as the main blocker if it's blocking
                if (shouldBlock && moveHitBuffer[i].distance < closestDist)
                {
                    closestDist = moveHitBuffer[i].distance;
                    closestHitId = i;
                }
            }

            // Identify the closest blocking collision
            if (closestHitId != -1)
            {
                hit = moveHitBuffer[closestHitId];
                numHitsTotal++;

                if (hitBufferSize > 0)
                {
                    int numToShift = numHitsTotal < hitBufferSize ? numHitsTotal : hitBufferSize - 1;
                    for (int p = numToShift; p >= 1; p--)
                        hitsOut[p] = hitsOut[p - 1];

                    hitsOut[0].normal = hit.normal;
                    hitsOut[0].collider = hit.collider;
                    hitsOut[0].hasPoint = true;
                    hitsOut[0].point = hit.point;
                }

                if (iteration < numIterations - 1)
                {
                    // use slidey slidey collision until the final one is hit
                    currentMovement += hit.normal * (-Vector3.Dot(hit.normal, currentMovement.normalized * (currentMovement.magnitude - hit.distance)) + skinThickness);
                }
                else
                {
                    // final collision: block further movement entirely
                    currentMovement = currentMovement.normalized * Mathf.Max(hit.distance - skinThickness, 0f);
                }
            }
            else
            {
                break; // we passed safely without collision
            }
        }

        // Do the move
        transform.position += currentMovement;

        MovementDebugStats.total.totalCollisionComputingTimeMicroseconds += methodStopwatch.ElapsedTicks * 1000000 / System.Diagnostics.Stopwatch.Frequency;

        // Call collision callbacks
        foreach (IMovementCollisionCallbacks collisions in movementCollisionCallbackBuffer)
            collisions.OnMovementCollidedBy(this, tickInfo);

        return numHitsTotal;
    }

    /// <summary>
    /// Performs a penetration-based movement
    /// </summary>
    public bool MovePenetration(Vector3 offset, out Hit hitOut, in TickInfo tickInfo, MoveFlags flags = MoveFlags.None)
    {
        int ret = MovePenetration(offset, genericSingleHitBuffer, tickInfo);
        hitOut = ret > 0 ? genericSingleHitBuffer[0] : default;
        return ret > 0;
    }

    /// <summary>
    /// Performs a penetration-based movement
    /// </summary>
    public int MovePenetration(Vector3 offset, Hit[] hitsOut, TickInfo tickInfo)
    {
        if (!enableCollision)
        {
            transform.position += offset;
            return 0; // done
        }

        // note that we move even if there's no offset. Penetration tests should remove us from walls and ceilings.
        methodStopwatch.Restart();

        float offsetMagnitude = offset.magnitude;
        Vector3 offsetNormalized = offsetMagnitude > 0f ? offset / offset.magnitude : Vector3.zero;
        float colliderExtentFromCentre = CalculateColliderExtentFromOrigin() + offsetMagnitude * 0.5f;
        Vector3 currentPosition = transform.position;
        Vector3 midPoint = transform.position + offset * 0.5f;
        int numHitsTotal = 0;
        int hitBufferSize = hitsOut != null ? hitsOut.Length : 0;

        movementCollisionCallbackBuffer.Clear();

        // detect nearby colliders
        // we use a sphere overlap with the sphere being in the centre of our path and extending to encompass the path plus our collider
        // QueryTriggerInteraction is Collide so that we can still send callbacks to triggers
        int numCollidersToTest = Physics.OverlapSphereNonAlloc(midPoint, colliderExtentFromCentre, nearbyColliderBuffer, blockingCollisionLayers, QueryTriggerInteraction.Collide);
        const int kNumIterationsPerStep = 3;

        MovementDebugStats.total.numOverlapTests++;

        for (float distanceStepped = 0f; distanceStepped <= offsetMagnitude; distanceStepped += maxCollisionStepSize)
        {
            Vector3 stepOffset = offsetNormalized * Mathf.Min(maxCollisionStepSize, offsetMagnitude - distanceStepped);
            Vector3 nextPosition = currentPosition + stepOffset;

            for (int iteration = 0; iteration < kNumIterationsPerStep; iteration++)
            {
                foreach (Collider collider in colliders)
                {
                    for (int i = 0; i < numCollidersToTest; i++)
                    {
                        if (nearbyColliderBuffer[i].gameObject == collider.gameObject)
                            continue; // please don't collide with yourself
                        if (gameObjectInstancesToIgnore.Contains(nearbyColliderBuffer[i].gameObject))
                            continue; // or ignored objects

                        MovementDebugStats.total.numPenetrationTests++;
                        if (Physics.ComputePenetration(collider, nextPosition, transform.rotation, nearbyColliderBuffer[i], nearbyColliderBuffer[i].transform.position, nearbyColliderBuffer[i].transform.rotation, out Vector3 hitDirection, out float hitDistance))
                        {
                            nearbyColliderBuffer[i].GetComponents<IMovementCollisionCallbacks>(movementCollisionCallbackComponentsBuffer);

                            bool isTrigger = nearbyColliderBuffer[i].isTrigger;
                            bool shouldBlock = !isTrigger && movementCollisionCallbackComponentsBuffer.Count == 0; // false if a trigger, and false if there are callbacks to evaluate first
                            foreach (IMovementCollisionCallbacks movementCollision in movementCollisionCallbackComponentsBuffer)
                            {
                                movementCollisionCallbackBuffer.Add(movementCollision);

                                if (!isTrigger)
                                    shouldBlock |= movementCollision.ShouldBlockMovement(this, moveHitBuffer[i]);
                            }

                            if (shouldBlock)
                            {
                                numHitsTotal++;
                                nextPosition += hitDirection * (hitDistance + skinThickness);

                                if (hitBufferSize > 0)
                                {
                                    int numToShift = numHitsTotal < hitBufferSize ? numHitsTotal : hitBufferSize;
                                    for (int p = numToShift; p >= 1; p--)
                                        hitsOut[p] = hitsOut[p - 1];

                                    hitsOut[0].normal = hitDirection;
                                    hitsOut[0].collider = nearbyColliderBuffer[i];
                                    hitsOut[0].hasPoint = false;
                                }

                                if (iteration == kNumIterationsPerStep - 1)
                                {
                                    goto FinishCollisionTest; // collision isn't being resolved; halt
                                }
                                else
                                {
                                    goto NextIteration;
                                }
                            }
                        }
                    }
                }

                break;
                NextIteration:;
            }

            currentPosition = nextPosition;
        }

        FinishCollisionTest:;
        transform.position = currentPosition;

        MovementDebugStats.total.totalCollisionComputingTimeMicroseconds += methodStopwatch.ElapsedTicks * 1000000 / System.Diagnostics.Stopwatch.Frequency;

        foreach (IMovementCollisionCallbacks collisions in movementCollisionCallbackBuffer)
            collisions.OnMovementCollidedBy(this, tickInfo);

        return numHitsTotal;
    }

    /// <summary>
    /// Performs a sweep test using our current colliders
    /// </summary>
    public int ColliderCast(RaycastHit[] hitsOut, Vector3 startPosition, Vector3 castDirection, float castMaxDistance, int layers, QueryTriggerInteraction queryTriggerInteraction, float pullback = 0f)
    {
        int numHits = 0;

        castDirection.Normalize();

        if (pullback > 0f)
        {
            startPosition -= castDirection * pullback;
            castMaxDistance += pullback;
        }

        Matrix4x4 toWorld = transform.localToWorldMatrix;
        Vector3 offset = startPosition - transform.position;
        toWorld.m03 += offset.x;
        toWorld.m13 += offset.y;
        toWorld.m23 += offset.z;

        Vector3 myUp = toWorld.MultiplyVector(Vector3.up);
        Vector3 myRight = toWorld.MultiplyVector(Vector3.right);
        Vector3 myForward = toWorld.MultiplyVector(Vector3.forward);
        Vector3 myLossyScale = transform.lossyScale;

        foreach (Collider collider in colliders)
        {
            int colliderNumHits = 0;

            if (collider is SphereCollider sphere)
            {
                float radius = sphere.radius * Mathf.Max(Mathf.Max(myLossyScale.x, myLossyScale.y), myLossyScale.z);

                colliderNumHits = Physics.SphereCastNonAlloc(
                    toWorld.MultiplyPoint(sphere.center),
                    radius,
                    castDirection,
                    colliderCastHitBuffer,
                    castMaxDistance,
                    layers,
                    queryTriggerInteraction);
            }
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 up = (capsule.direction == 0 ? myRight : (capsule.direction == 1 ? myUp : myForward)) * (Mathf.Max(capsule.height * 0.5f - capsule.radius, 0));
                Vector3 center = toWorld.MultiplyPoint(capsule.center);

                colliderNumHits = Physics.CapsuleCastNonAlloc(
                    center + up, center - up,
                    capsule.radius * Mathf.Max(Mathf.Max(myLossyScale.x, myLossyScale.y), myLossyScale.z),
                    castDirection,
                    colliderCastHitBuffer,
                    castMaxDistance,
                    layers,
                    queryTriggerInteraction);
            }
            else if (collider is BoxCollider box)
            {
                colliderNumHits = Physics.BoxCastNonAlloc(
                    toWorld.MultiplyPoint(box.center),
                    new Vector3(box.size.x * myLossyScale.x, box.size.y * myLossyScale.y, box.size.z * myLossyScale.z) * 0.5f,
                    castDirection,
                    colliderCastHitBuffer,
                    collider.transform.rotation, // todo: consistency between toWorld and collider to world
                    castMaxDistance,
                    layers,
                    queryTriggerInteraction);
            }
            else
            {
                continue; // couldn't detect collider type
            }

            MovementDebugStats.total.numSweepTests++;

            for (int i = 0; i < colliderNumHits && numHits < hitsOut.Length; i++)
            {
                if (colliderCastHitBuffer[i].collider.gameObject == collider.gameObject)
                    continue; // avoid colliding with self...
                if (Vector3.Dot(colliderCastHitBuffer[i].normal, castDirection) > 0.01f)
                    continue; // in pullback collisions, don't collide with things we wouldn't actually crash into
                if (colliderCastHitBuffer[i].distance <= 0f)
                    continue; // this type of collision normally seems to happen when we're already inside them, which doesn't work nicely with pullback collisions

                // don't collide with children of self either
                for (Transform currentTransform = colliderCastHitBuffer[i].transform; currentTransform != null; currentTransform = currentTransform.parent)
                {
                    if (currentTransform.gameObject == collider.gameObject)
                        goto Skip;
                }

                // test passed, add this hit
                hitsOut[numHits] = colliderCastHitBuffer[i];
                hitsOut[numHits].distance -= pullback;
                numHits++;
                Skip:;
            }
        }

        return numHits;
    }

    private float CalculateColliderExtentFromOrigin()
    {
        float extent = 0f;
        foreach (Collider collider in colliders)
        {
            if (collider is SphereCollider colliderAsSphere)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsSphere.center + Vector3.up * colliderAsSphere.radius)));
            }
            else if (collider is CapsuleCollider colliderAsCapsule)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsCapsule.center + Vector3.up * (colliderAsCapsule.height * 0.5f + colliderAsCapsule.radius))));
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsCapsule.center - Vector3.up * (colliderAsCapsule.height * 0.5f + colliderAsCapsule.radius))));
            }
            else if (collider is BoxCollider colliderAsBox)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsBox.center + colliderAsBox.size)));
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsBox.center - colliderAsBox.size)));
            }
        }

        return extent;
    }

    private void OnValidate()
    {
        int numValidColliders = 0;

        foreach (Collider collider in colliders)
        {
            // == null because user might be adding one
            if (collider == null || (collider is CapsuleCollider) || (collider is SphereCollider) || (collider is BoxCollider))
                numValidColliders++;
        }

        if (numValidColliders != colliders.Length)
        {
            Collider[] collidersNew = new Collider[numValidColliders];
            int numRemoved = 0;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (!((colliders[i] is CapsuleCollider) || (colliders[i] is SphereCollider) || (colliders[i] is BoxCollider)))
                    numRemoved++;
                else
                    collidersNew[i - numRemoved] = colliders[i];
            }

            colliders = collidersNew;
            Debug.LogError($"{gameObject.name}: Movement component currently only supports CapsuleColliders, SphereColliders and BoxColliders. Incompatible colliders have been removed.", gameObject);
        }

        if (colliders.Length == 0 && enableCollision)
            Debug.LogWarning($"{gameObject.name}: Movement component collider list is empty. Collision will be ignored.", gameObject);
    }
}

public interface IMovementCollisionCallbacks
{
    /// <summary>
    /// Should return whether the object blocks the movement. It may be called multiple times per frame. It should not change the object's state.
    /// Allows something to pass through conditionally.
    /// The hit parameter expresses the collision that took place.
    /// If this object is a trigger, this function won't get called and the object will not block.
    /// </summary>
    bool ShouldBlockMovement(Movement source, in RaycastHit hit);

    void OnMovementCollidedBy(Movement source, TickInfo tickInfo);
}

public static class MovementDebugStats
{
    public struct Snapshot
    {
        public int numOverlapTests;
        public int numSweepTests;
        public int numPenetrationTests;
        public long totalCollisionComputingTimeMicroseconds;

        public Snapshot Since(Snapshot earlierSnapshot)
        {
            return new Snapshot()
            {
                numOverlapTests = numOverlapTests - earlierSnapshot.numOverlapTests,
                numSweepTests = numSweepTests - earlierSnapshot.numSweepTests,
                numPenetrationTests = numPenetrationTests - earlierSnapshot.numPenetrationTests,
                totalCollisionComputingTimeMicroseconds = totalCollisionComputingTimeMicroseconds - earlierSnapshot.totalCollisionComputingTimeMicroseconds
            };
        }

        public override string ToString()
        {
            return $"numOverlaps: {numOverlapTests} numSweeps: {numSweepTests} numPenetrations: {numPenetrationTests} totalMs: {((double)totalCollisionComputingTimeMicroseconds / 1000d).ToString("F3")}";
        }
    }

    public static Snapshot total;
}