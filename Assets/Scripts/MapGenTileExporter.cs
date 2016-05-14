using ActionStreetMap.Core.Tiling.Models;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MapGen {
    public class MapGenTileExporter {
        private const string k_exportDir = "Exported";
        private const string k_exportPath = "Assets/" + k_exportDir;

        private readonly MapGenManager m_manager;

        public MapGenTileExporter(MapGenManager manager) {
            m_manager = manager;

            /* Delete any existing exported assets. */
            AssetDatabase.DeleteAsset(k_exportPath);
            AssetDatabase.CreateFolder("Assets", k_exportDir);
        }

        private class CombinedMesh {
            public int totalVertexCount = 0;
            public List<CombineInstance> combines = new List<CombineInstance>();
        };

        public void Export(Tile tile) {
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
            newObject.isStatic = true;

            /* Generate new objects out of all the combined meshes. */
            int meshIndex = 0;
            int wastage = 0;
            foreach (CombinedMesh combinedMesh in meshes) {
                Debug.LogWarning(String.Format(
                    "Creating mesh {0} with {1} vertices",
                    meshIndex, combinedMesh.totalVertexCount));

                wastage += 65534 - combinedMesh.totalVertexCount;

                GameObject childObject = new GameObject("Mesh " + meshIndex);
                meshIndex++;
                childObject.transform.parent = newObject.transform;

                Mesh mesh = new Mesh();
                mesh.CombineMeshes(combinedMesh.combines.ToArray());
                mesh.Optimize();

                // FIXME: use sharedMesh when exporting
                childObject.AddComponent<MeshFilter>().mesh = mesh;
                childObject.AddComponent<MeshRenderer>().sharedMaterial = m_manager.CombinedMaterial;

                if (m_manager.AddColliders)
                    childObject.AddComponent<MeshCollider>();
            }

            origObject.SetActive(false);
            newObject.SetActive(true);

            Debug.LogWarning(String.Format(
                "Exported tile {0} {1}, wasted {2} vertices",
                tileIndexX, tileIndexY, wastage));
        }

        public void CreateAsset(UnityEngine.Object asset, string name) {
            AssetDatabase.CreateAsset(asset, k_exportPath + "/" + name);
        }
    }
}
