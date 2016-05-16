using ActionStreetMap.Core.Tiling.Models;
using ActionStreetMap.Infrastructure.Reactive;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace MapGen {
    public class MapGenTileExporter {
        private class SourceMesh {
            public Mesh mesh;
            public Matrix4x4 transform;
            public int vertexCount;
        };

        private class CombinedMesh {
            public int totalVertexCount = 0;
            public List<CombineInstance> combines = new List<CombineInstance>();
        };

        /** Path to export world to. */
        private const string k_exportDir = "Generated";
        private const string k_exportPath = "Assets/" + k_exportDir;

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
                AssetDatabase.DeleteAsset(k_exportPath);
                AssetDatabase.CreateFolder("Assets", k_exportDir);
                AssetDatabase.CreateFolder(k_exportPath, "Meshes");
            }

            /* Create the container object. */
            m_containerObject = new GameObject("Map");
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
            var origMeshes = new List<SourceMesh>();
            Observable.Start(
                () => {
                    Component[] components = origObject.GetComponentsInChildren<MeshFilter>();
                    foreach (MeshFilter meshFilter in components) {
                        if (m_manager.FilterInfoNodes && meshFilter.name.StartsWith("info Node"))
                            continue;

                        SourceMesh origMesh = new SourceMesh();
                        origMesh.mesh = meshFilter.mesh;
                        origMesh.transform = meshFilter.transform.localToWorldMatrix;
                        origMesh.vertexCount = meshFilter.mesh.vertexCount;
                        origMeshes.Add(origMesh);
                    }
                },
                Scheduler.MainThread).Wait();

            /*
             * Try to combine all the meshes into smaller meshes. Basic bin
             * packing problem, use a greedy first-fit algorithm.
             */
            foreach (SourceMesh origMesh in origMeshes) {
                Mesh mesh = origMesh.mesh;
                int vertexCount = origMesh.vertexCount;

                /* Reduce the complexity of the mesh. */
                if (m_manager.EnableMeshReduction)
                    mesh = new MeshReducer().Reduce(mesh, 0.9f, out vertexCount);

                /* Find a mesh that can fit this one. */
                CombinedMesh targetMesh = null;
                foreach (CombinedMesh existingMesh in m_meshes) {
                    if (existingMesh.totalVertexCount + vertexCount <= 65534) {
                        targetMesh = existingMesh;
                        break;
                    }
                }
                if (targetMesh == null) {
                    targetMesh = new CombinedMesh();
                    m_meshes.Add(targetMesh);
                }

                CombineInstance combine = new CombineInstance();
                combine.mesh = mesh;
                combine.transform = origMesh.transform;
                targetMesh.combines.Add(combine);
                targetMesh.totalVertexCount += vertexCount;
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

                if (m_manager.EnableExport) {
                    /* Save the mesh as a new asset. */
                    MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                    AssetDatabase.CreateAsset(
                        mesh,
                        String.Format("{0}/Meshes/Tile_{1}_{2}_{3}.asset",
                            k_exportPath, m_tileIndexX, m_tileIndexY, meshIndex));
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                /* Create an object to render this mesh. */
                GameObject childObject = new GameObject("Mesh " + meshIndex);
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

        public void Finish() {
            if (m_manager.EnableExport)
                Observable.Start(() => ExportPrefab(), Scheduler.MainThread).Wait();

            Debug.LogWarning("Map generation complete");
        }

        private void ExportPrefab() {
            /* Create a prefab out of the world. */
            UnityEngine.Object prefab = PrefabUtility.CreateEmptyPrefab(k_exportPath + "/Map.prefab");
            PrefabUtility.ReplacePrefab(m_containerObject, prefab, ReplacePrefabOptions.ConnectToPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
