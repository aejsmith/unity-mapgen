using System;
using System.Collections.Generic;
using ActionStreetMap.Infrastructure.Reactive;
using UnityEngine;
using UnityEngine.Assertions;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace MapGen {
    /** Class to reduce mesh complexity. */
    public class MeshReducer {
        private class Vertex {
            public Vector3 position;
            public Color colour;
            public Vector3 normal;
            public int newIndex;
            public List<Triangle> triangles = new List<Triangle>();

            public bool IsDead() {
                return triangles.Count == 0;
            }

            public override bool Equals(object obj) {
                Vertex other = obj as Vertex;
                return
                    position == other.position &&
                    normal == other.normal &&
                    (Vector4)colour == (Vector4)other.colour;
            }

            public override int GetHashCode() {
                int hash = 0;
                hash += position.GetHashCode();
                hash += normal.GetHashCode();
                hash += colour.GetHashCode();
                return hash;
            }
        };

        private class Triangle {
            public Vertex[] vertices = new Vertex[3];
            public Vector3 normal;

            public void ComputeNormal() {
                normal = Vector3.Cross(
                    vertices[1].position - vertices[0].position,
                    vertices[2].position - vertices[1].position);
                normal.Normalize();
            }

            public bool Adjacent(Triangle other) {
                for (int i = 0; i < 3; i++) {
                    for (int j = 0; j < 3; j++) {
                        if (vertices[i].position == other.vertices[j].position)
                            return true;
                    }
                }

                return false;
            }
        };

        private class Face {
            public Vector3 normal;
            public List<Triangle> triangles = new List<Triangle>();
        }

        private Dictionary<Vertex, Vertex> m_vertices;
        private int m_liveVertexCount;
        private List<Triangle> m_triangles;

        /** Reduce a mesh.
         * @param origMesh       Mesh to reduce.
         * @param meshType       Type of the mesh.
         * @param newVertexCount Output vertex count.
         * @return               Reduced mesh. */
        public Mesh Reduce(Mesh origMesh, MeshType meshType, out int newVertexCount) {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Vector3[] origVertices = null;
            Vector3[] origNormals = null;
            Color[] origColours = null;
            int[] origTriangles = null;

            /* Get the original data from the main thread. */
            Observable.Start(
                () => {
                    origVertices = origMesh.vertices;
                    origNormals = origMesh.normals;
                    origColours = origMesh.colors;
                    origTriangles = origMesh.triangles;
                },
                Scheduler.MainThread).Wait();

            /*
             * Add triangle and vertex data. This filters out any duplicate
             * vertices.
             */
            m_vertices = new Dictionary<Vertex, Vertex>();
            m_triangles = new List<Triangle>(origTriangles.Length / 3);
            for (int i = 0; i < origTriangles.Length; i += 3) {
                /*
                 * ASM's triangle data sometimes has some zeros in for some
                 * reason. Skip these.
                 */
                if (origTriangles[i] == 0 && origTriangles[i + 1] == 0 && origTriangles[i + 2] == 0)
                    continue;

                Assert.IsTrue(origTriangles[i] != origTriangles[i + 1]);
                Assert.IsTrue(origTriangles[i] != origTriangles[i + 2]);
                Assert.IsTrue(origTriangles[i + 1] != origTriangles[i + 2]);

                Triangle triangle = new Triangle();

                for (int j = 0; j < 3; j++) {
                    Vertex vertex = new Vertex();
                    vertex.position = origVertices[origTriangles[i + j]];
                    vertex.normal = origNormals[origTriangles[i + j]];
                    vertex.colour = origColours[origTriangles[i + j]];

                    Vertex exist;
                    if (m_vertices.TryGetValue(vertex, out exist)) {
                        triangle.vertices[j] = exist;
                    } else {
                        m_vertices.Add(vertex, vertex);
                        triangle.vertices[j] = vertex;
                    }
                }

                triangle.ComputeNormal();

                for (int j = 0; j < 3; j++)
                    triangle.vertices[j].triangles.Add(triangle);

                m_triangles.Add(triangle);
            }

            m_liveVertexCount = m_vertices.Count;

            switch (meshType) {
                case MeshType.Floor:
                    ReduceFloor();
                    break;
                case MeshType.Wall:
                    ReduceWall();
                    break;
            }

            /* Create new vertex data. */
            Vector3[] newVertices = new Vector3[m_liveVertexCount];
            Vector3[] newNormals = new Vector3[m_liveVertexCount];
            Color[] newColours = new Color[m_liveVertexCount];
            int newIndex = 0;
            foreach (Vertex vertex in m_vertices.Values) {
                if (vertex.IsDead())
                    continue;

                vertex.newIndex = newIndex++;

                newVertices[vertex.newIndex] = vertex.position;
                newNormals[vertex.newIndex] = vertex.normal;
                newColours[vertex.newIndex] = vertex.colour;
            }

            /* Create new triangle data. */
            int[] newTriangles = new int[m_triangles.Count * 3];
            for (int i = 0, offset = 0; i < m_triangles.Count; i++) {
                newTriangles[offset++] = m_triangles[i].vertices[0].newIndex;
                newTriangles[offset++] = m_triangles[i].vertices[1].newIndex;
                newTriangles[offset++] = m_triangles[i].vertices[2].newIndex;
            }

            /* Create new mesh on the main thread. */
            Mesh newMesh = Observable.Start(
                () => {
                    Mesh mesh = new Mesh();
                    mesh.vertices = newVertices;
                    mesh.normals = newNormals;
                    mesh.colors = newColours;
                    mesh.triangles = newTriangles;
                    mesh.Optimize();
                    return mesh;
                },
                Scheduler.MainThread).Wait();

            stopwatch.Stop();

            Debug.Log(String.Format(
                "Reduced {0}/{1} to {2}/{3} in {4}ms",
                origVertices.Length, origTriangles.Length / 3, m_liveVertexCount,
                m_triangles.Count, stopwatch.ElapsedMilliseconds));

            newVertexCount = m_liveVertexCount;
            return newMesh;
        }

        private void ReduceFloor() {
            /*
             * What ActionStreetMap calls the floor actually contains the roof
             * and the floor, and the floor is pretty much useless because it's
             * not visible. So what we do here is strip it out. There is some
             * variation in the Y coordinates of the floor and roof surfaces,
             * so what we do is get the minimum and maximum Y coordinates, then
             * strip out anything that's close to the minimum.
             */

            float minimumY = float.MaxValue;
            float maximumY = float.MinValue;

            foreach (var vertex in m_vertices.Values) {
                minimumY = Mathf.Min(minimumY, vertex.position.y);
                maximumY = Mathf.Max(maximumY, vertex.position.y);
            }

            foreach (var vertex in m_vertices.Values) {
                if (vertex.position.y - minimumY <= 2.0f)
                    KillVertex(vertex);
            }
        }

        private void ReduceWall() {
            var faces = new List<Face>();

            foreach (var triangle in m_triangles) {
                Face targetFace = null;

                for (int i = 0; i < faces.Count; i++) {
                    if (faces[i].normal != triangle.normal)
                        continue;

                    foreach (var otherTriangle in faces[i].triangles) {
                        if (triangle.Adjacent(otherTriangle)) {
                            if (targetFace == null) {
                                targetFace = faces[i];
                                targetFace.triangles.Add(triangle);
                            } else {
                                targetFace.triangles.AddRange(faces[i].triangles);
                                faces.RemoveAt(i--);
                            }

                            break;
                        }
                    }
                }

                if (targetFace == null) {
                    targetFace = new Face();
                    targetFace.normal = triangle.normal;
                    targetFace.triangles.Add(triangle);
                    faces.Add(targetFace);
                }
            }

            foreach (var face in faces) {
                Vertex minX = null;
                Vertex maxX = null;
                Vertex minY = null;
                Vertex maxY = null;
                Vertex minZ = null;
                Vertex maxZ = null;

                foreach (var triangle in face.triangles) {
                    for (int i = 0; i < 3; i++) {
                        if (minX == null || triangle.vertices[i].position.x < minX.position.x)
                            minX = triangle.vertices[i];
                        if (maxX == null || triangle.vertices[i].position.x > maxX.position.x)
                            maxX = triangle.vertices[i];
                        if (minY == null || triangle.vertices[i].position.y < minY.position.y)
                            minY = triangle.vertices[i];
                        if (maxY == null || triangle.vertices[i].position.y > maxY.position.y)
                            maxY = triangle.vertices[i];
                        if (minZ == null || triangle.vertices[i].position.z < minZ.position.z)
                            minZ = triangle.vertices[i];
                        if (maxZ == null || triangle.vertices[i].position.z > maxZ.position.z)
                            maxZ = triangle.vertices[i];

                        /* We're completely replacing the face. */
                        KillTriangle(triangle);
                    }
                }

                /*
                 * Check which of the X and Z axes has the greatest difference
                 * between minimum and maximum. This indicates which direction
                 * the face extends in. Also need to figure out which way to
                 * wind the vertices based on the normal.
                 */
                Vertex min;
                Vertex max;
                bool clockwise;
                if (maxX.position.x - minX.position.x > maxZ.position.z - minZ.position.z) {
                    min = minX;
                    max = maxX;
                    clockwise = face.normal.z < 0;
                } else {
                    min = minZ;
                    max = maxZ;
                    clockwise = face.normal.x > 0;
                }

                Func<Vertex, Vertex, Vertex> makeVertex = (Vertex from, Vertex y) => {
                    Vertex vertex = new Vertex();
                    vertex.position = new Vector3(from.position.x, y.position.y, from.position.z);
                    vertex.colour = from.colour;
                    vertex.normal = face.normal;

                    Vertex exist;
                    m_liveVertexCount++;
                    if (m_vertices.TryGetValue(vertex, out exist)) {
                        return exist;
                    } else {
                        m_vertices.Add(vertex, vertex);
                        return vertex;
                    }
                };

                /* Make a new face. */
                Vertex bottomLeft = makeVertex(min, minY);
                Vertex topLeft = makeVertex(min, maxY);
                Vertex bottomRight = makeVertex(max, minY);
                Vertex topRight = makeVertex(max, maxY);

                Triangle newTriangle = new Triangle();
                int i0 = (clockwise) ? 0 : 2;
                int i1 = (clockwise) ? 1 : 1;
                int i2 = (clockwise) ? 2 : 0;
                newTriangle.vertices[i0] = bottomLeft; bottomLeft.triangles.Add(newTriangle);
                newTriangle.vertices[i1] = topLeft; topLeft.triangles.Add(newTriangle);
                newTriangle.vertices[i2] = bottomRight; bottomRight.triangles.Add(newTriangle);
                newTriangle.normal = face.normal;
                m_triangles.Add(newTriangle);
                newTriangle = new Triangle();
                newTriangle.vertices[i0] = bottomRight; bottomLeft.triangles.Add(newTriangle);
                newTriangle.vertices[i1] = topLeft; topLeft.triangles.Add(newTriangle);
                newTriangle.vertices[i2] = topRight; topRight.triangles.Add(newTriangle);
                newTriangle.normal = face.normal;
                m_triangles.Add(newTriangle);
            }

            Debug.LogWarning("Face count = " + faces.Count);
        }

        private void KillVertex(Vertex vertex) {
            while (vertex.triangles.Count > 0)
                KillTriangle(vertex.triangles[0]);
        }

        private void KillTriangle(Triangle triangle) {
            m_triangles.Remove(triangle);

            foreach (var vertex in triangle.vertices) {
                bool wasDead = vertex.IsDead();
                vertex.triangles.Remove(triangle);
                if (!wasDead && vertex.IsDead())
                    m_liveVertexCount--;
            }
        }
    }
}
