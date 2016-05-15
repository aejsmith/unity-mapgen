using ActionStreetMap.Core.Tiling.Models;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MapGen {
    public class MapGenTileExporter {
        /** Path to export world to. */
        private const string k_exportDir = "Generated";
        private const string k_exportPath = "Assets/" + k_exportDir;

        private readonly MapGenManager m_manager;

        /** Container object for all generated tiles. */
        private GameObject m_containerObject;

        public MapGenTileExporter(MapGenManager manager) {
            m_manager = manager;

            /* Delete any existing exported assets, create new folder. */
            AssetDatabase.DeleteAsset(k_exportPath);
            AssetDatabase.CreateFolder("Assets", k_exportDir);
            AssetDatabase.CreateFolder(k_exportPath, "Meshes");

            /* Create the container object. */
            m_containerObject = new GameObject("Map");
        }

        private class CombinedMesh {
            public int totalVertexCount = 0;
            public List<CombineInstance> combines = new List<CombineInstance>();
        };

        public void ExportTile(Tile tile) {
            int tileIndexX = (int)(tile.MapCenter.X / m_manager.TileSize);
            int tileIndexY = (int)(tile.MapCenter.Y / m_manager.TileSize);

            Debug.LogWarning(String.Format("Exporting tile {0} {1}", tileIndexX, tileIndexY));

            GameObject origObject = tile.GameObject.GetComponent<GameObject>();

            /* Mesh combining state. */
            var meshes = new List<CombinedMesh>();

            /*
             * Get all child objects with meshes and try to combine them into
             * smaller meshes. Basic bin packing problem, use a greedy first-fit
             * algorithm.
             */
            Component[] components = origObject.GetComponentsInChildren<MeshFilter>();
            foreach (MeshFilter meshFilter in components) {
                /* Don't want to export the info nodes. */
                if (m_manager.FilterInfoNodes && meshFilter.name.StartsWith("info Node"))
                    continue;

                /* Find a mesh that can fit this one. */
                CombinedMesh targetMesh = null;
                foreach (CombinedMesh existingMesh in meshes) {
                    if (existingMesh.totalVertexCount + meshFilter.mesh.vertexCount <= 65534) {
                        targetMesh = existingMesh;
                        break;
                    }
                }
                if (targetMesh == null) {
                    targetMesh = new CombinedMesh();
                    meshes.Add(targetMesh);
                }

                CombineInstance combine = new CombineInstance();
                combine.mesh = meshFilter.mesh;
                combine.transform = meshFilter.transform.localToWorldMatrix;
                targetMesh.combines.Add(combine);
                targetMesh.totalVertexCount += meshFilter.mesh.vertexCount;
            }

            /* Create a container for the merged tile. */
            GameObject newObject = new GameObject("Tile " + tileIndexX + " " + tileIndexY);
            newObject.SetActive(false);
            newObject.transform.parent = m_containerObject.transform;
            newObject.isStatic = true;

            /* Generate new objects out of all the combined meshes. */
            int meshIndex = 0;
            int wastage = 0;
            foreach (CombinedMesh combinedMesh in meshes) {
                Debug.LogWarning(String.Format(
                    "Creating mesh {0} with {1} vertices",
                    meshIndex, combinedMesh.totalVertexCount));

                wastage += 65534 - combinedMesh.totalVertexCount;

                Mesh mesh = new Mesh();
                mesh.CombineMeshes(combinedMesh.combines.ToArray());
                mesh.Optimize();

                /* Save the mesh as a new asset. */
                MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                AssetDatabase.CreateAsset(
                    mesh,
                    String.Format("{0}/Meshes/Tile_{1}_{2}_{3}.asset", k_exportPath, tileIndexX, tileIndexY, meshIndex));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                /* Create an object to render this mesh. */
                GameObject childObject = new GameObject("Mesh " + meshIndex);
                childObject.transform.parent = newObject.transform;
                childObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                childObject.AddComponent<MeshRenderer>().sharedMaterial = m_manager.CombinedMaterial;

                if (m_manager.AddColliders)
                    childObject.AddComponent<MeshCollider>();

                meshIndex++;
            }

            origObject.SetActive(false);
            newObject.SetActive(true);

            Debug.LogWarning(String.Format(
                "Exported tile {0} {1}, wasted {2} vertices",
                tileIndexX, tileIndexY, wastage));
        }

        public void Finish() {
            /* Create a prefab out of the world. */
            UnityEngine.Object prefab = PrefabUtility.CreateEmptyPrefab(k_exportPath + "/Map.prefab");
            PrefabUtility.ReplacePrefab(m_containerObject, prefab, ReplacePrefabOptions.ConnectToPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogWarning("Map export complete");
        }
    }
}
