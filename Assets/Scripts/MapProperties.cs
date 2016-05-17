using UnityEngine;

namespace MapGen {
    /**
     * Class added to a generated map to include the properties it was generated
     * with.
     */
    public class MapProperties : MonoBehaviour {
        public double CentreLatitude;
        public double CentreLongitude;
        public float TileSize;
        public Vector2 WorldSize;
        public Vector2 DetailedWorldSize;
    }
}
