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

        /** Global instance of the class. */
        public static MapProperties Instance { get { return m_instance; } }
        private static MapProperties m_instance;

        void Awake() {
            /*
             * Store a global pointer to this instance rather than using
             * GameObject methods to look it up whenever we need it, as Unity
             * docs recommend against doing this for performance reasons.
             */
            m_instance = this;
        }

        void OnApplicationQuit() {
            m_instance = null;
        }
    }
}
