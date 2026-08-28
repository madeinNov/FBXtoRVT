using System;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 커넥터 연결 공통 유틸.
    ///
    /// 여러 기능이 공통으로 쓰는 "한쪽(Sub) 객체를 움직여 다른 쪽(Main) 커넥터에 맞춘 뒤 연결"
    /// 동작을 한 곳에 모아둔다. Main 쪽 객체는 절대 움직이지 않는다.
    /// </summary>
    public static class ConnectorHelper
    {
        /// <summary>
        /// subConn 을 가진 객체(subElemId)를 이동·회전시켜 mainConn 에 맞춘 뒤 연결한다.
        /// mainConn 쪽 객체는 움직이지 않는다.
        /// </summary>
        public static void AlignAndConnect(Document doc, Connector mainConn, Connector subConn, ElementId subElemId)
        {
            XYZ mainOrigin = mainConn.Origin;
            XYZ mainDir = mainConn.CoordinateSystem.BasisZ;   // Main 커넥터가 바깥으로 향하는 방향
            XYZ subOrigin = subConn.Origin;
            XYZ subDir = subConn.CoordinateSystem.BasisZ;      // Sub 커넥터가 바깥으로 향하는 방향

            // 1) Sub 커넥터 방향을 Main 방향의 "반대"로 향하도록 회전
            //    (두 커넥터는 서로 마주봐야 하므로)
            XYZ desiredDir = mainDir.Negate();
            double angle = subDir.AngleTo(desiredDir);

            if (angle > 1e-9)
            {
                XYZ axis;
                if (Math.Abs(angle - Math.PI) < 1e-9)
                {
                    // 정확히 반대 방향이면 외적이 0 → 임의의 수직축을 회전축으로 사용
                    axis = GetPerpendicular(subDir);
                }
                else
                {
                    axis = subDir.CrossProduct(desiredDir).Normalize();
                }

                // subOrigin 을 지나는 회전축 (회전해도 subOrigin 위치는 그대로 유지됨)
                Line rotationAxis = Line.CreateUnbound(subOrigin, axis);
                ElementTransformUtils.RotateElement(doc, subElemId, rotationAxis, angle);
            }

            // 2) Sub 커넥터 원점을 Main 커넥터 원점으로 평행이동
            //    (회전은 subOrigin 을 중심으로 했으므로 subOrigin 은 변하지 않았음)
            XYZ moveVector = mainOrigin - subOrigin;
            if (!moveVector.IsZeroLength())
            {
                ElementTransformUtils.MoveElement(doc, subElemId, moveVector);
            }

            // 3) 두 커넥터 연결
            if (!mainConn.IsConnectedTo(subConn))
            {
                mainConn.ConnectTo(subConn);
            }
        }

        /// <summary>
        /// 주어진 벡터에 수직인 단위벡터 하나를 반환.
        /// </summary>
        private static XYZ GetPerpendicular(XYZ v)
        {
            // v 와 나란하지 않은 기준축을 골라 외적
            XYZ reference = (Math.Abs(v.X) < 0.9) ? XYZ.BasisX : XYZ.BasisY;
            return v.CrossProduct(reference).Normalize();
        }
    }
}
