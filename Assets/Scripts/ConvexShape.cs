using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ConvexShape : ISerializationCallbackReceiver
{
    // Unity cannot serialize nested containers such as List<Vector3[]> directly.
    // Preserve the actual cut geometry across script reloads, including preview pieces.
    [System.Serializable]
    private class SerializedFace
    {
        public Vector3[] vertices;
        public bool cut;
    }

    [SerializeField, HideInInspector] private List<SerializedFace> savedFaces;
    [System.NonSerialized]
    public List<Vector3[]> faces;
    // Parallel to faces: true for faces made by a cut. Paper/Piece draws them with section hatching.
    // Null means no face was made by a cut.
    [System.NonSerialized]
    public List<bool> cutFaces;
    const float epsilon = 0.0001f;

    static readonly Color32 cutFaceColor = new Color32(255, 0, 0, 255);
    static readonly Color32 outerFaceColor = new Color32(0, 0, 0, 255);

    public void OnBeforeSerialize()
    {
        if (faces == null) return;
        savedFaces = new List<SerializedFace>(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            savedFaces.Add(new SerializedFace { vertices = faces[i], cut = IsCutFace(i) });
        }
    }

    public void OnAfterDeserialize()
    {
        faces = new List<Vector3[]>();
        cutFaces = new List<bool>();
        if (savedFaces == null) return;
        foreach (var face in savedFaces)
        {
            faces.Add(face.vertices);
            cutFaces.Add(face.cut);
        }
    }

    public ConvexShape(List<Vector3[]> faceList)
    {
        faces = faceList;
    }

    public ConvexShape(List<Vector3[]> faceList, List<bool> cutList)
    {
        faces = faceList;
        cutFaces = cutList;
    }

    /// <summary>True if face <paramref name="index"/> was made by a cut.</summary>
    public bool IsCutFace(int index)
    {
        return cutFaces != null && index < cutFaces.Count && cutFaces[index];
    }

    public static ConvexShape MakeBox(Vector3 size)
    {
        Vector3 half = size * 0.5f;
        List<Vector3[]> boxFaces = new List<Vector3[]>();
        boxFaces.Add(new Vector3[] { GetCorner(half, 1, -1, -1), GetCorner(half, 1, 1, -1), GetCorner(half, 1, 1, 1), GetCorner(half, 1, -1, 1) });
        boxFaces.Add(new Vector3[] { GetCorner(half, -1, -1, -1), GetCorner(half, -1, -1, 1), GetCorner(half, -1, 1, 1), GetCorner(half, -1, 1, -1) });
        boxFaces.Add(new Vector3[] { GetCorner(half, -1, 1, -1), GetCorner(half, -1, 1, 1), GetCorner(half, 1, 1, 1), GetCorner(half, 1, 1, -1) });
        boxFaces.Add(new Vector3[] { GetCorner(half, -1, -1, -1), GetCorner(half, 1, -1, -1), GetCorner(half, 1, -1, 1), GetCorner(half, -1, -1, 1) });
        boxFaces.Add(new Vector3[] { GetCorner(half, -1, -1, 1), GetCorner(half, 1, -1, 1), GetCorner(half, 1, 1, 1), GetCorner(half, -1, 1, 1) });
        boxFaces.Add(new Vector3[] { GetCorner(half, -1, -1, -1), GetCorner(half, -1, 1, -1), GetCorner(half, 1, 1, -1), GetCorner(half, 1, -1, -1) });
        return new ConvexShape(boxFaces);
    }

    static Vector3 GetCorner(Vector3 half, float x, float y, float z)
    {
        return new Vector3(x * half.x, y * half.y, z * half.z);
    }

    public ConvexShape ApplyMatrix(Matrix4x4 matrix)
    {
        List<Vector3[]> newFaces = new List<Vector3[]>(faces.Count);
        foreach (Vector3[] face in faces)
        {
            Vector3[] newFace = new Vector3[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                newFace[i] = matrix.MultiplyPoint3x4(face[i]);
            }
            newFaces.Add(newFace);
        }
        return new ConvexShape(newFaces, cutFaces != null ? new List<bool>(cutFaces) : null);
    }

    public ConvexShape MoveBy(Vector3 offset)
    {
        return ApplyMatrix(Matrix4x4.Translate(offset));
    }

    public Vector3 GetCenter()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Vector3[] face in faces)
        {
            foreach (Vector3 point in face)
            {
                sum += point;
                count++;
            }
        }
        if (count > 0) return sum / count;
        return Vector3.zero;
    }

    public Bounds GetBounds()
    {
        Bounds bounds = new Bounds(faces[0][0], Vector3.zero);
        foreach (Vector3[] face in faces)
        {
            foreach (Vector3 point in face)
            {
                bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    public float GetVolume()
    {
        Vector3 center = GetCenter();
        float volume = 0f;
        foreach (Vector3[] face in faces)
        {
            for (int i = 1; i < face.Length - 1; i++)
            {
                volume += Mathf.Abs(Vector3.Dot(face[0] - center, Vector3.Cross(face[i] - center, face[i + 1] - center))) / 6f;
            }
        }
        return volume;
    }

    public bool Cut(Vector3 normal, float planeDist, out ConvexShape frontShape, out ConvexShape backShape)
    {
        frontShape = null;
        backShape = null;
        List<Vector3[]> frontFaces = new List<Vector3[]>();
        List<Vector3[]> backFaces = new List<Vector3[]>();
        List<bool> frontCut = new List<bool>();
        List<bool> backCut = new List<bool>();
        List<Vector3> cutPoints = new List<Vector3>();

        for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            Vector3[] face = faces[faceIndex];
            bool faceWasCut = IsCutFace(faceIndex);
            int count = face.Length;
            float[] dist = new float[count];
            bool onPlane = true;
            for (int i = 0; i < count; i++)
            {
                dist[i] = Vector3.Dot(normal, face[i]) - planeDist;
                if (Mathf.Abs(dist[i]) > epsilon) onPlane = false;
            }
            if (onPlane) continue;

            List<Vector3> frontPoints = new List<Vector3>();
            List<Vector3> backPoints = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                Vector3 pointA = face[i];
                Vector3 pointB = face[next];
                float distA = dist[i];
                float distB = dist[next];

                if (distA > epsilon)
                {
                    frontPoints.Add(pointA);
                }
                else if (distA < -epsilon)
                {
                    backPoints.Add(pointA);
                }
                else
                {
                    frontPoints.Add(pointA);
                    backPoints.Add(pointA);
                    cutPoints.Add(pointA);
                }

                if ((distA > epsilon && distB < -epsilon) || (distA < -epsilon && distB > epsilon))
                {
                    Vector3 hitPoint = Vector3.Lerp(pointA, pointB, distA / (distA - distB));
                    frontPoints.Add(hitPoint);
                    backPoints.Add(hitPoint);
                    cutPoints.Add(hitPoint);
                }
            }
            if (frontPoints.Count >= 3)
            {
                frontFaces.Add(frontPoints.ToArray());
                frontCut.Add(faceWasCut);
            }
            if (backPoints.Count >= 3)
            {
                backFaces.Add(backPoints.ToArray());
                backCut.Add(faceWasCut);
            }
        }

        List<Vector3> points = new List<Vector3>();
        foreach (Vector3 p in cutPoints)
        {
            bool alreadyAdded = false;
            foreach (Vector3 q in points)
            {
                if ((p - q).sqrMagnitude < 1e-8f)
                {
                    alreadyAdded = true;
                    break;
                }
            }
            if (!alreadyAdded) points.Add(p);
        }
        if (points.Count < 3 || frontFaces.Count < 3 || backFaces.Count < 3) return false;

        Vector3 center = Vector3.zero;
        foreach (Vector3 p in points)
        {
            center += p;
        }
        center /= points.Count;

        Vector3 axis1 = Vector3.right;
        if (Mathf.Abs(normal.x) >= 0.9f) axis1 = Vector3.up;
        axis1 = (axis1 - normal * Vector3.Dot(axis1, normal)).normalized;
        Vector3 axis2 = Vector3.Cross(normal, axis1);

        float[] angles = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            angles[i] = Mathf.Atan2(Vector3.Dot(p - center, axis2), Vector3.Dot(p - center, axis1));
        }
        for (int i = 0; i < points.Count - 1; i++)
        {
            for (int j = 0; j < points.Count - 1 - i; j++)
            {
                if (angles[j] > angles[j + 1])
                {
                    float tempAngle = angles[j];
                    angles[j] = angles[j + 1];
                    angles[j + 1] = tempAngle;

                    Vector3 tempPoint = points[j];
                    points[j] = points[j + 1];
                    points[j + 1] = tempPoint;
                }
            }
        }

        // The new cap face on each side is a cut face.
        frontFaces.Add(points.ToArray());
        frontCut.Add(true);
        backFaces.Add(points.ToArray());
        backCut.Add(true);
        frontShape = new ConvexShape(frontFaces, frontCut);
        backShape = new ConvexShape(backFaces, backCut);
        return true;
    }

    public Mesh MakeMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        // For the paper shaders: vertex colour (1,0,0) marks cut faces, UV0 is the position on the face
        // in world units from the face's corner, UV1 is the face's size.
        List<Color32> colors = new List<Color32>();
        List<Vector2> faceUVs = new List<Vector2>();
        List<Vector2> faceSizes = new List<Vector2>();
        Vector3 center = GetCenter();

        for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            Vector3[] face = faces[faceIndex];
            Vector3 normal = GetFaceNormal(face);
            Vector3 faceCenter = Vector3.zero;
            foreach (Vector3 p in face)
            {
                faceCenter += p;
            }
            faceCenter /= face.Length;

            if (Vector3.Dot(normal, faceCenter - center) < 0f)
            {
                Vector3[] flipped = new Vector3[face.Length];
                for (int i = 0; i < face.Length; i++)
                {
                    flipped[i] = face[face.Length - 1 - i];
                }
                face = flipped;
                normal = -normal;
            }

            Vector3 axisU = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, normal).normalized;
            Vector3 axisV = Vector3.Cross(normal, axisU).normalized;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 p in face)
            {
                Vector2 uv = new Vector2(Vector3.Dot(p, axisU), Vector3.Dot(p, axisV));
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
            Color32 faceColor = IsCutFace(faceIndex) ? cutFaceColor : outerFaceColor;

            int startIndex = vertices.Count;
            foreach (Vector3 p in face)
            {
                vertices.Add(p);
                normals.Add(normal);
                colors.Add(faceColor);
                faceUVs.Add(new Vector2(Vector3.Dot(p, axisU), Vector3.Dot(p, axisV)) - min);
                faceSizes.Add(max - min);
            }
            for (int i = 1; i < face.Length - 1; i++)
            {
                triangles.Add(startIndex);
                triangles.Add(startIndex + i);
                triangles.Add(startIndex + i + 1);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "CutPiece";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetUVs(0, faceUVs);
        mesh.SetUVs(1, faceSizes);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 GetFaceNormal(Vector3[] face)
    {
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < face.Length; i++)
        {
            Vector3 a = face[i];
            Vector3 b = face[(i + 1) % face.Length];
            normal.x += (a.y - b.y) * (a.z + b.z);
            normal.y += (a.z - b.z) * (a.x + b.x);
            normal.z += (a.x - b.x) * (a.y + b.y);
        }
        if (normal.sqrMagnitude > 1e-12f) return normal.normalized;
        return Vector3.up;
    }
}
