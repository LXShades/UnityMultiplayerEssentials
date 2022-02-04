using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extended What-You-See-is-What-You-Get 3D debug draw functions supporting various shapes, without Gizmos or editor required
/// </summary>
public static class DebugDraw
{
    private class DebugShape
    {
        public List<Vector3> points = new List<Vector3>();
        public Color color = Color.white;
    }

    private static Material lineMaterial
    {
        get
        {
            if (_lineMaterial == null)
            {
                Shader shaderToUse = Shader.Find("Unlit/UnityMultiplayerEssentials/ThickLineShader");

                if (shaderToUse == null)
                {
                    Debug.LogWarning("[UnityMultiplayerEssentials.DebugDraw] For better debug lines in builds, add Unlit/UnityMultiplayerEssentials/ThickLineShader to Always Included Shaders. Using fallback.");
                    shaderToUse = Shader.Find("Hidden/Internal-Colored");
                }
                _lineMaterial = new Material(shaderToUse);
                _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            return _lineMaterial;
        }
    }
    private static Material _lineMaterial;

    const float kRadsInCircle = Mathf.PI * 2f;

    private static List<DebugShape> currentDebugShapes = new List<DebugShape>();
    private static List<DebugShape> debugShapePool = new List<DebugShape>();

    /// <summary>
    /// Draws a line between start and end in world coordinates
    /// </summary>
    public static void DrawLine(Vector3 start, Vector3 end, Color color)
    {
        DebugShape output = GetNewShape(color);

        output.points.Add(start);
        output.points.Add(end);

        RequestDrawThisFrame();
    }

    /// <summary>
    /// Draws a sphere of the given world position and radius
    /// </summary>
    public static void DrawSphere(Vector3 position, float radius, Color color, int numLongitudeSegments = 4, int numCircleSegments = 16)
    {
        float radsPerLongitude = kRadsInCircle / numLongitudeSegments;
        float radsPerCircleSegment = kRadsInCircle / numCircleSegments;
        DebugShape output = GetNewShape(color);

        // longitude circles
        for (float longitude = 0f; longitude < kRadsInCircle; longitude += radsPerLongitude)
        {
            Vector3 lo = new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);
            Vector3 last = position + new Vector3(0f, radius, 0f);

            for (float latitude = radsPerCircleSegment; latitude < kRadsInCircle - 0.001f; latitude += radsPerCircleSegment)
            {
                Vector3 next = position + new Vector3(0f, Mathf.Cos(latitude) * radius, 0f) + lo * Mathf.Sin(latitude);

                output.points.Add(last);
                output.points.Add(next);

                last = next;
            }
        }

        // centre circle
        Vector3 last2 = position + new Vector3(0f, 0f, radius);
        for (float longitude = radsPerCircleSegment; longitude < kRadsInCircle - 0.001f; longitude += radsPerCircleSegment)
        {
            Vector3 next = position + new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);

            output.points.Add(last2);
            output.points.Add(next);

            last2 = next;
        }

        RequestDrawThisFrame();
    }

    /// <summary>
    /// Draws a top-down circle of the given world position and radius
    /// </summary>
    public static void DrawCircle(Vector3 position, float radius, Color color, int numSegments = 16)
    {
        DebugShape output = GetNewShape(color);
        float radsPerLongitude = kRadsInCircle / numSegments;
        Vector3 last = position + new Vector3(0f, 0f, radius);

        for (float longitude = radsPerLongitude; longitude < kRadsInCircle - 0.001f; longitude += radsPerLongitude)
        {
            Vector3 next = position + new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);
            output.points.Add(last);
            output.points.Add(next);
            last = next;
        }

        RequestDrawThisFrame();
    }

    /// <summary>
    /// Draws a capsule of the given world start/end points and radius
    /// </summary>
    public static void DrawCapsule(Vector3 start, Vector3 end, float radius, Color color, int numLongitudeSegments = 4, int numTipSegments = 8)
    {
        DebugShape output = GetNewShape(color);

        Vector3 localUp = (end - start).normalized;
        Vector3 localRight = Vector3.Cross(new Vector3(0.99f, 0.99f, 0.99f), localUp).normalized;
        Vector3 localForward = Vector3.Cross(localUp, localRight).normalized;
        float radsPerLongitude = kRadsInCircle / numLongitudeSegments;
        float radsPerCircleSegment = kRadsInCircle / numTipSegments / 4f;

        // Unity capsules start on the tips instead of the centre of the sphere at the start and end, which is what we'll be using
        Vector3 sphereCentresToTips = localUp * Mathf.Min(radius, Vector3.Distance(start, end) / 2f);
        start += sphereCentresToTips;
        end -= sphereCentresToTips;

        // Begin draw
        for (float longitude = 0f; longitude < kRadsInCircle - 0.001f; longitude += radsPerLongitude)
        {
            Vector3 toCircleEdge = (localRight * Mathf.Sin(longitude) + localForward * Mathf.Cos(longitude)) * radius;
            Vector3 lastStartSeg = start + toCircleEdge;
            Vector3 lastEndSeg = end + toCircleEdge;

            // connect the start and end
            output.points.Add(lastStartSeg);
            output.points.Add(lastEndSeg);

            // curve both the start and end tips
            for (float rads = radsPerCircleSegment; rads < kRadsInCircle / 4f; rads += radsPerCircleSegment)
            {
                Vector3 up = localUp * (Mathf.Sin(rads) * radius), fwd = toCircleEdge * Mathf.Cos(rads);
                Vector3 nextStartSeg = start - up + fwd;
                Vector3 nextEndSeg = end + up + fwd;

                output.points.Add(lastStartSeg);
                output.points.Add(nextStartSeg);
                output.points.Add(lastEndSeg);
                output.points.Add(nextEndSeg);

                lastStartSeg = nextStartSeg;
                lastEndSeg = nextEndSeg;
            }
        }

        RequestDrawThisFrame();
    }

    private static void RequestDrawThisFrame()
    {
        Camera.onPostRender -= OnFinalRenderDebugShapes;
        Camera.onPostRender += OnFinalRenderDebugShapes;
    }

    private static DebugShape GetNewShape(Color color)
    {
        if (debugShapePool.Count > 0)
        {
            DebugShape output = debugShapePool[debugShapePool.Count - 1];
            debugShapePool.RemoveAt(debugShapePool.Count - 1);
            currentDebugShapes.Add(output);

            output.points.Clear();
            output.color = color;

            return output;
        }
        else
        {
            DebugShape output = new DebugShape();
            currentDebugShapes.Add(output);
            return output;
        }
    }

    private static void OnFinalRenderDebugShapes(Camera cam)
    {
        lineMaterial.SetPass(0);

        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        GL.Begin(GL.LINES);

        foreach (DebugShape shape in currentDebugShapes)
        {
            GL.Color(shape.color);

            for (int i = 0, e = shape.points.Count / 2 * 2; i < e; i++)
            {
                GL.Vertex(shape.points[i]);
            }
        }

        GL.End();
        GL.PopMatrix();

        debugShapePool.AddRange(currentDebugShapes);
        currentDebugShapes.Clear();

        Camera.onPostRender -= OnFinalRenderDebugShapes;
    }
}