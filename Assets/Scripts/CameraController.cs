using UnityEngine;
using System.Collections;

namespace MapGen {
    /** Class implementing camera controls. */
    public class CameraController : MonoBehaviour {
        /** Movement speeds. */
        public float RotateSpeed = 1;
        public float ZoomSpeed = 5;
        public float TranslateSpeed = 1;

        /** Current rotation. */
        private float m_horizRotation;
        private float m_vertRotation;

        public void Start() {
            m_horizRotation = transform.eulerAngles.y;
            m_vertRotation = transform.eulerAngles.x;
        }

        public void Update() {
            /* Right-click and drag = Rotate. */
            if (Input.GetMouseButton(1)) {
                float horizInc = Input.GetAxis("Mouse X") * MovementSpeed(RotateSpeed);
                float vertInc = -Input.GetAxis("Mouse Y") * MovementSpeed(RotateSpeed);
                if (horizInc != 0 || vertInc != 0) {
                    IncrementAngle(ref m_horizRotation, horizInc);
                    IncrementAngle(ref m_vertRotation, vertInc);
                    transform.rotation = Quaternion.Euler(m_vertRotation, m_horizRotation, 0);
                }
            }

            /* Scroll wheel = Zoom. */
            float zoomInc = Input.GetAxis("Mouse ScrollWheel") * MovementSpeed(ZoomSpeed);
            if (zoomInc != 0)
                transform.Translate(Vector3.forward * zoomInc);

            /* Middle mouse = Translate. */
            if (Input.GetMouseButton(2)) {
                float xInc = Input.GetAxis("Mouse X") * MovementSpeed(TranslateSpeed);
                float zInc = Input.GetAxis("Mouse Y") * MovementSpeed(TranslateSpeed);
                if (xInc != 0 || zInc != 0) {
                    transform.Translate(
                        Quaternion.Euler(0, m_horizRotation, 0) * new Vector3(xInc, 0, zInc),
                        Space.World);
                }
            }
        }

        /** Update an angle handling wraparound.
         * @param angle         Angle to update.
         * @param increment     Value to increment angle by.
         * @return              Updated angle. */
        private static void IncrementAngle(ref float angle, float increment) {
            angle += increment;
            if (angle < 0)
                angle += 360;
            if (angle > 360)
                angle -= 360;
        }

        /** Get a movement speed based on the state of the shift key.
         * @param baseSpeed     Base speed.
         * @return              Double base speed if shift pressed, unmodified speed
         *                      otherwise. */
        private static float MovementSpeed(float baseSpeed) {
            return (Input.GetKey(KeyCode.LeftShift))
                ? baseSpeed * 2
                : baseSpeed;
        }
    }
}
