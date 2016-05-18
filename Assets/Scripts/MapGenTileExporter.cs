using ActionStreetMap.Core.Tiling.Models;
using ActionStreetMap.Infrastructure.Reactive;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace MapGen {
    public class MapGenTileExporter {
        private enum MeshType {
            Generic,
            Water,
        };

        private class SourceMesh {
            public MeshType type;
            public Mesh mesh;
            public Matrix4x4 transform;
            public int vertexCount;
        };

        private class CombinedMesh {
            public MeshType type;
            public int totalVertexCount;
            public List<CombineInstance> combines = new List<CombineInstance>();
        };

        /** Path to export world to. */
        private const string ExportParent = "Assets/Map";
        private const string ExportDir = "Generated";
        private const string ExportPath = ExportParent + "/" + ExportDir;

        private readonly MapGenManager m_manager;

        /** Container object for all generated tiles. */
        private GameObject m_containerObject;

        /** Current tile state. */
        private Tile m_tile;
        private int m_tileIndexX;
        private int m_tileIndexY;
        private List<CombinedMesh> m_meshes;

        public MapGenTileExporter(MapGenManager manager) {
            m_manager = manager;

            if (m_manager.EnableExport) {
                /* Delete any existing exported assets, create new folder. */
                AssetDatabase.DeleteAsset(ExportPath);
                AssetDatabase.CreateFolder(ExportParent, ExportDir);
                AssetDatabase.CreateFolder(ExportPath, "Meshes");
            }

            /* Create the container object. */
            m_containerObject = new GameObject("Map");
            MapProperties properties = m_containerObject.AddComponent<MapProperties>();
            properties.CentreLatitude = m_manager.CentreLatitude;
            properties.CentreLongitude = m_manager.CentreLongitude;
            properties.TileSize = m_manager.TileSize;
            properties.WorldSize = m_manager.WorldSize;
            properties.DetailedWorldSize = m_manager.DetailedWorldSize;
        }

        public void ExportTile(Tile tile) {
            Assert.IsNull(m_tile);

            /* Initialise state. */
            m_tile = tile;
            m_tileIndexX = (int)(m_tile.MapCenter.X / m_manager.TileSize);
            m_tileIndexY = (int)(m_tile.MapCenter.Y / m_manager.TileSize);
            m_meshes = new List<CombinedMesh>();

            Debug.LogWarning(String.Format("Generating tile {0} {1}", m_tileIndexX, m_tileIndexY));

            /*
             * Perform mesh simplification and combination off the main thread
             * as this is quite a lengthy process and if we run it on the main
             * thread we hang up the GUI.
             */
            GenerateMeshes();

            /*
             * Object generation must be done on the main thread as objects can
             * only be created there.
             */
            Observable.Start(() => GenerateAndExportObjects(), Scheduler.MainThread).Wait();

            m_tile = null;
            m_meshes = null;
        }

        private void GenerateMeshes() {
            /*
             * Get the set of all meshes to combine. All of this must be done on
             * the main thread.
             */
            GameObject origObject = m_tile.GameObject.GetComponent<GameObject>();
            var sourceMeshes = new List<SourceMesh>();
            Observable.Start(
                () => {
                    Component[] components = origObject.GetComponentsInChildren<MeshFilter>();
                    foreach (MeshFilter meshFilter in components) {
                        if (m_manager.FilterInfoNodes && meshFilter.name.StartsWith("info Node"))
                            continue;

                        SourceMesh sourceMesh = new SourceMesh();

                        if (meshFilter.name == "water") {
                            sourceMesh.type = MeshType.Water;
                        } else {
                            sourceMesh.type = MeshType.Generic;
                        }

                        sourceMesh.mesh = meshFilter.mesh;
                        sourceMesh.transform = meshFilter.transform.localToWorldMatrix;
                        sourceMesh.vertexCount = meshFilter.mesh.vertexCount;
                        sourceMeshes.Add(sourceMesh);
                    }
                },
                Scheduler.MainThread).Wait();

            /*
             * Try to combine all the meshes into smaller meshes. Basic bin
             * packing problem, use a greedy first-fit algorithm.
             */
            foreach (SourceMesh sourceMesh in sourceMeshes) {
                Mesh mesh = sourceMesh.mesh;
                int vertexCount = sourceMesh.vertexCount;

                /* Reduce the complexity of the mesh. */
                if (m_manager.EnableMeshReduction)
                    mesh = new MeshReducer().Reduce(mesh, 0.9f, out vertexCount);

                MeshType combinedType =
                    (m_manager.EnableWaterSeparation && sourceMesh.type == MeshType.Water)
                        ? MeshType.Water
                        : MeshType.Generic;

                /* Find a mesh that can fit this one. */
                CombinedMesh combinedMesh = null;
                foreach (CombinedMesh existingMesh in m_meshes) {
                    if (existingMesh.type != combinedType) {
                        continue;
                    } else if (existingMesh.totalVertexCount + vertexCount > 65534) {
                        continue;
                    }

                    combinedMesh = existingMesh;
                    break;
                }
                if (combinedMesh == null) {
                    combinedMesh = new CombinedMesh();
                    combinedMesh.type = combinedType;
                    m_meshes.Add(combinedMesh);
                }

                CombineInstance combine = new CombineInstance();
                combine.mesh = mesh;
                combine.transform = sourceMesh.transform;
                combinedMesh.combines.Add(combine);
                combinedMesh.totalVertexCount += vertexCount;
            }
        }
        
        private void GenerateAndExportObjects() {
            /* Create a container for the merged tile. */
            GameObject newObject = new GameObject("Tile " + m_tileIndexX + " " + m_tileIndexY);
            newObject.SetActive(false);
            newObject.transform.parent = m_containerObject.transform;
            newObject.isStatic = true;

            /* Generate new objects out of all the combined meshes. */
            int meshIndex = 0;
            int wastage = 0;
            foreach (CombinedMesh combinedMesh in m_meshes) {
                Debug.LogWarning(String.Format(
                    "Creating mesh {0} with {1} vertices",
                    meshIndex, combinedMesh.totalVertexCount));

                wastage += 65534 - combinedMesh.totalVertexCount;

                Mesh mesh = new Mesh();
                mesh.CombineMeshes(combinedMesh.combines.ToArray());
                mesh.Optimize();

                if (combinedMesh.type == MeshType.Water) {
                    /*
                     * Flatten the water surface. The reason for the water
                     * separation is so that it can have Unity's water shader
                     * placed on it. This does not look right with a non-flat
                     * surface.
                     */
                    FlattenWaterSurface(mesh);
                }

                if (m_manager.EnableExport) {
                    /* Save the mesh as a new asset. */
                    MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                    AssetDatabase.CreateAsset(
                        mesh,
                        String.Format("{0}/Meshes/Tile_{1}_{2}_{3}.asset",
                            ExportPath, m_tileIndexX, m_tileIndexY, meshIndex));
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                /* Create an object to render this mesh. */
                GameObject childObject = new GameObject(
                    "Mesh " + meshIndex + " (" + combinedMesh.type.ToString() + ")");
                childObject.transform.parent = newObject.transform;
                childObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                childObject.AddComponent<MeshRenderer>().sharedMaterial = m_manager.CombinedMaterial;

                if (m_manager.AddColliders)
                    childObject.AddComponent<MeshCollider>();

                meshIndex++;
            }

            m_tile.GameObject.GetComponent<GameObject>().SetActive(false);
            newObject.SetActive(true);

            Debug.LogWarning(String.Format(
                "Generated tile {0} {1}, wasted {2} vertices",
                m_tileIndexX, m_tileIndexY, wastage));
        }

        private void FlattenWaterSurface(Mesh mesh) {
            Vector3[] vertices = mesh.vertices;

            /*
             * Get minimum value. This should ensure matching between this tile
             * and adjacent ones.
             */
            float height = float.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
                height = Math.Min(vertices[i].y, height);

            for (int i = 0; i < vertices.Length; i++)
                vertices[i].y = height;

            mesh.vertices = vertices;
        }

        public void Finish() {
            if (m_manager.EnableExport)
                Observable.Start(() => ExportPrefab(), Scheduler.MainThread).Wait();

            Debug.LogWarning("Map generation complete");
        }

        private void ExportPrefab() {
            /* Create a prefab out of the world. */
            UnityEngine.Object prefab = PrefabUtility.CreateEmptyPrefab(ExportPath + "/Map.prefab");
            PrefabUtility.ReplacePrefab(m_containerObject, prefab, ReplacePrefabOptions.ConnectToPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
