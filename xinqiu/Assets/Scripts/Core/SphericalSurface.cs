// @trace MECH-002
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 球面几何工具。球心固定在世界原点，半径由 WhiteboxConfig 提供，不写死在代码里。
    /// 位移始终投影到切平面、站立方向始终为法线、位置钳制回球壳（MECH-002 / 策划案 §12）。
    /// </summary>
    public static class SphericalSurface
    {
        public static Vector3 Up(Vector3 position, Vector3 center)
        {
            Vector3 v = position - center;
            return v.sqrMagnitude < 0.000001f ? Vector3.up : v.normalized;
        }

        public static Vector3 Point(Vector3 center, Vector3 direction, float radius, float height)
        {
            return center + direction.normalized * (radius + height);
        }

        /// <summary>把一个物体摆到球面：本地 up 对齐法线，本地 forward 对齐切平面朝向。</summary>
        public static void Place(Transform t, Vector3 center, Vector3 direction,
            float radius, float height, Vector3 forwardHint)
        {
            Vector3 up = direction.sqrMagnitude < 0.000001f ? Vector3.up : direction.normalized;
            t.position = center + up * (radius + height);

            Vector3 fwd = Vector3.ProjectOnPlane(forwardHint, up);
            if (fwd.sqrMagnitude < 0.000001f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (fwd.sqrMagnitude < 0.000001f) fwd = Vector3.ProjectOnPlane(Vector3.right, up);
            t.rotation = Quaternion.LookRotation(fwd.normalized, up);
        }

        /// <summary>沿球面切线移动一段弧长，返回仍在球壳上的新点。</summary>
        public static Vector3 MoveOnSurface(Vector3 center, Vector3 surfacePoint,
            Vector3 tangent, float distance)
        {
            Vector3 up = Up(surfacePoint, center);
            Vector3 dir = Vector3.ProjectOnPlane(tangent, up);
            if (dir.sqrMagnitude < 0.000001f) return surfacePoint;
            dir.Normalize();

            float r = Mathf.Max(0.01f, Vector3.Distance(surfacePoint, center));
            float angle = distance / r;
            return center + (up * Mathf.Cos(angle) + dir * Mathf.Sin(angle)) * r;
        }

        /// <summary>确定性球面方向（斐波那契/黄金角分布用）。</summary>
        public static Vector3 Direction(float y01, float angleRad)
        {
            float y = Mathf.Clamp(y01 * 2f - 1f, -0.92f, 0.92f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(Mathf.Cos(angleRad) * r, y, Mathf.Sin(angleRad) * r).normalized;
        }
    }
}
