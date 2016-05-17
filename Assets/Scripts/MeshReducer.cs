using System;
using System.Collections.Generic;
using ActionStreetMap.Infrastructure.Reactive;
using UnityEngine;
using UnityEngine.Assertions;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace MapGen {
    /**
     * Class to a reduce the complexity of a mesh.
     *
     * This class implements a mesh reduction algorithm, based on Stan Melax's
     * Progressive Mesh type Polygon Reduction Algorithm, found here:
     * https://github.com/melax/sandbox/blob/master/bunnylod/progmesh.cpp
     */
    public class MeshReducer {
        private class Vertex {
            public Vector3 position;
            public Vector2 uv;
            public Color colour;
            public Vector3 normal;
            public int newIndex;
            public HashSet<Vertex> neighbours = new HashSet<Vertex>();
            public List<Triangle> triangles = new List<Triangle>();
            public float cost;
            public Vertex collapse;

            public void RemoveIfNonNeighbour(Vertex vertex) {
                if (neighbours.Contains(vertex)) {
                    foreach (Triangle triangle in triangles) {
                        if (triangle.HasVertex(vertex))
                            return;
                    }

                    neighbours.Remove(vertex);
                }
            }

            public override bool Equals(object obj) {
                Vertex other = obj as Vertex;
                return
                    position == other.position &&
                    normal == other.normal &&
                    uv == other.uv &&
                    (Vector4)colour == (Vector4)other.colour;
            }

            public override int GetHashCode() {
                int hash = 0;
                hash += position.GetHashCode();
                hash += normal.GetHashCode();
                hash += uv.GetHashCode();
                hash += colour.GetHashCode();
                return hash;
            }
        };

        private class Triangle {
            public Vertex[] vertices = new Vertex[3];
            public Vector3 normal;

            public bool HasVertex(Vertex vertex) {
                return vertex == vertices[0] || vertex == vertices[1] || vertex == vertices[2];
            }

            public void ReplaceVertex(Vertex oldVertex, Vertex newVertex) {
                Assert.IsTrue(oldVertex != null && newVertex != null);
                Assert.IsTrue(oldVertex == vertices[0] || oldVertex == vertices[1] || oldVertex == vertices[2]);
                Assert.IsTrue(newVertex != vertices[0] && newVertex != vertices[1] && newVertex != vertices[2]);

                if (oldVertex == vertices[0]) {
                    vertices[0] = newVertex;
                } else if (oldVertex == vertices[1]) {
                    vertices[1] = newVertex;
                } else {
                    vertices[2] = newVertex;
                }

                oldVertex.triangles.Remove(this);
                Assert.IsTrue(!newVertex.triangles.Contains(this));
                newVertex.triangles.Add(this);

                for (int i = 0; i < 3; i++) {
                    oldVertex.RemoveIfNonNeighbour(vertices[i]);
                    vertices[i].RemoveIfNonNeighbour(oldVertex);
                }

                for (int i = 0; i < 3; i++) {
                    Assert.IsTrue(vertices[i].triangles.Contains(this));

                    for (int j = 0; j < 3; j++) {
                        if (i != j)
                            vertices[i].neighbours.Add(vertices[j]);
                    }
                }

                ComputeNormal();
            }

            public void ComputeNormal() {
                normal = Vector3.Cross(
                    vertices[1].position - vertices[0].position,
                    vertices[2].position - vertices[1].position);
                normal.Normalize();
            }
        };

        private Dictionary<Vertex, Vertex> m_vertices;
        private List<Triangle> m_triangles;

        /** Reduce a mesh.
         * @param origMesh       Mesh to reduce.
         * @param factor         Factor to reduce by (between 0 and 1).
         * @param newVertexCount Output vertex count.
         * @return               Reduced mesh. */
        public Mesh Reduce(Mesh origMesh, float factor, out int newVertexCount) {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Vector3[] origVertices = null;
            Vector3[] origNormals = null;
            Vector2[] origUV = null;
            Color[] origColours = null;
            int[] origTriangles = null;

            /* Get the original data from the main thread. */
            Observable.Start(
                () => {
                    origVertices = origMesh.vertices;
                    origNormals = origMesh.normals;
                    origUV = origMesh.uv;
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
                    vertex.uv = origUV[origTriangles[i + j]];
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

                for (int j = 0; j < 3; j++) {
                    triangle.vertices[j].triangles.Add(triangle);
                    for (int k = 0; k < 3; k++) {
                        /* Uniqueness is enforced as neighbours is a HashSet. */
                        if (k != j)
                            triangle.vertices[j].neighbours.Add(triangle.vertices[k]);
                    }
                }

                m_triangles.Add(triangle);
            }

            /* Compute all edge collapse costs. */
            foreach (Vertex vertex in m_vertices.Values)
                ComputeEdgeCostAtVertex(vertex);

            /* Calculate target number of vertices. */
            int targetVertices = (int)((float)origVertices.Length * factor);

            /* Collapse until we reach this target. */
            //while (m_vertices.Count > targetVertices) {
            //    Vertex vertex = MinimumCostEdge();
            //    Collapse(vertex, vertex.collapse);
            //}

            /* Create new vertex data. */
            Vector3[] newVertices = new Vector3[m_vertices.Count];
            Vector3[] newNormals = new Vector3[m_vertices.Count];
            Vector2[] newUV = new Vector2[m_vertices.Count];
            Color[] newColours = new Color[m_vertices.Count];
            int newIndex = 0;
            foreach (Vertex vertex in m_vertices.Values) {
                vertex.newIndex = newIndex++;

                newVertices[vertex.newIndex] = vertex.position;
                newNormals[vertex.newIndex] = vertex.normal;
                newUV[vertex.newIndex] = vertex.uv;
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
                    mesh.uv = newUV;
                    mesh.colors = newColours;
                    mesh.triangles = newTriangles;
                    mesh.RecalculateNormals();
                    mesh.Optimize();
                    return mesh;
                },
                Scheduler.MainThread).Wait();

            stopwatch.Stop();

            Debug.Log(String.Format(
                "Reduced {0}/{1} to {2}/{3} in {4}ms",
                origVertices.Length, origTriangles.Length / 3, m_vertices.Count,
                m_triangles.Count, stopwatch.ElapsedMilliseconds));

            newVertexCount = m_vertices.Count;
            return newMesh;
        }

        private void ComputeEdgeCostAtVertex(Vertex vertex) {
            if (vertex.neighbours.Count == 0) {
                vertex.cost = -0.01f;
                return;
            }

            vertex.cost = float.MaxValue;

            foreach (Vertex neighbour in vertex.neighbours) {
                float cost = ComputeEdgeCollapseCost(vertex, neighbour);
                if (cost < vertex.cost) {
                    vertex.collapse = neighbour;
                    vertex.cost = cost;
                }
            }
        }

        private float ComputeEdgeCollapseCost(Vertex u, Vertex v) {
            float edgeLength = Vector3.Distance(v.position, u.position);

            var sides = new List<Triangle>();
            foreach (Triangle i in u.triangles) {
                if (i.HasVertex(v))
                    sides.Add(i);
            }

            float curvature = 0;

            foreach (Triangle i in u.triangles) {
                float minCurvature = 1.0f;

                foreach (Triangle j in sides) {
                    float dotProduct = Vector3.Dot(i.normal, j.normal);
                    minCurvature = Math.Min(minCurvature, (1 - dotProduct) / 2.0f);
                }

                curvature = Math.Max(curvature, minCurvature);
            }

            return edgeLength * curvature;
        }

        private Vertex MinimumCostEdge() {
// FIXME: If too slow optimise this.
            Vertex min = null;
            foreach (Vertex vertex in m_vertices.Values) {
                if (min == null || vertex.cost < min.cost)
                    min = vertex;
            }

            return min;
        }

        private void Collapse(Vertex u, Vertex v) {
            if (v == null) {
                RemoveVertex(u);
                return;
            }

            var origNeighbours = new List<Vertex>(u.neighbours);

            for (int i = u.triangles.Count - 1; i >= 0; i--) {
                if (u.triangles[i].HasVertex(v))
                    RemoveTriangle(u.triangles[i]);
            }

            for (int i = u.triangles.Count - 1; i >= 0; i--)
                u.triangles[i].ReplaceVertex(u, v);

            RemoveVertex(u);

            foreach (Vertex vertex in origNeighbours)
                ComputeEdgeCostAtVertex(vertex);
        }

        private void RemoveVertex(Vertex vertex) {
            Assert.AreEqual(vertex.triangles.Count, 0);

            foreach (Vertex neighbour in vertex.neighbours)
                neighbour.neighbours.Remove(vertex);

            vertex.neighbours.Clear();

            m_vertices.Remove(vertex);
        }

        private void RemoveTriangle(Triangle triangle) {
            for (int i = 0; i < 3; i++)
                triangle.vertices[i].triangles.Remove(triangle);

            for (int i = 0; i < 3; i++) {
                int i2 = (i + 1) % 3;
                triangle.vertices[i ].RemoveIfNonNeighbour(triangle.vertices[i2]);
                triangle.vertices[i2].RemoveIfNonNeighbour(triangle.vertices[i ]);
            }

            m_triangles.Remove(triangle);

        }
    }
}
