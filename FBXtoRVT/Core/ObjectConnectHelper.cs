using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "연결" 기능의 핵심 로직.
    ///
    /// [무엇을 하는 기능인가]
    /// 사용자가 객체 두 개를 차례로 고르면, 두 객체의 <b>열린 커넥터 중 서로 가장 가까운 한 쌍</b>을 연결한다.
    /// 첫 번째 객체는 절대 움직이지 않고, <b>두 번째 객체가 움직여서</b> 붙는다.
    ///
    /// [두 번째 객체가 움직이는 방식]
    ///  - 배관이 아닌 객체(피팅 / 장비 등) : 회전 + 이동으로 커넥터를 맞춘다. (<see cref="ConnectorHelper.AlignAndConnect"/>)
    ///  - 배관                            : 이동이 아니라 <b>배관 길이를 늘리거나 줄여서(Stretch)</b> 끝을 맞춘다.
    ///                                      옆으로 어긋난 만큼은 배관을 평행이동해서 맞춘다.
    ///                                      배관 축과 상대 커넥터가 마주보지 않아 Stretch 가 안 되면,
    ///                                      배관 길이를 그대로 두고 다른 피팅류처럼 회전 + 이동으로 붙인다.
    ///
    /// [실패 처리]
    /// 이 기능은 대화상자를 띄우지 않는다. 조건이 맞지 않으면 false 를 돌려주고 로그에만 남긴다.
    /// (호출한 쪽에서 Transaction 을 롤백한다)
    /// </summary>
    public static class ObjectConnectHelper
    {
        // 배관 축과 상대 커넥터 방향이 "서로 마주본다" 고 볼 각도 허용오차(라디안). 약 2도.
        private const double FacingAngleTolerance = 0.035;

        // 옆 어긋남이 이보다 작으면 평행이동하지 않는다. (0.1mm)
        private static readonly double LateralTolerance = ElementUtils.MmToFeet(0.1);

        /// <summary>
        /// 두 객체를 연결한다. (외부에서 Transaction 을 열고 호출)
        /// 첫 번째 객체는 그대로 두고 두 번째 객체를 움직인다.
        /// </summary>
        /// <returns>연결했으면 true. 조건이 맞지 않아 아무것도 하지 않았으면 false.</returns>
        public static bool Connect(Document doc, Element firstElem, Element secondElem)
        {
            if (firstElem == null || secondElem == null) return false;

            // 같은 객체면 커넥터 계산을 하지 않고 바로 끝낸다.
            if (firstElem.Id == secondElem.Id)
            {
                LogUtils.Log("같은 객체를 두 번 골라서 연결하지 않습니다.");
                return false;
            }

            Connector mainConn, subConn;
            if (!FindNearestOpenConnectorPair(firstElem, secondElem, out mainConn, out subConn))
            {
                LogUtils.Log($"열린 커넥터가 없어 연결하지 않습니다. 첫 객체 Id={firstElem.Id} 둘째 객체 Id={secondElem.Id}");
                return false;
            }

            LogUtils.Log($"===== 연결 시작. 첫 객체 Id={firstElem.Id} 커넥터={FormatXyz(mainConn.Origin)} / " +
                $"둘째 객체 Id={secondElem.Id} 커넥터={FormatXyz(subConn.Origin)} =====");

            if (secondElem is Pipe secondPipe)
            {
                // 배관은 되도록 이동이 아니라 Stretch 로 붙인다.
                if (StretchPipeToConnector(doc, secondPipe, subConn, mainConn))
                {
                    LogUtils.Log("===== 연결 종료. (배관 Stretch) =====");
                    return true;
                }

                // Stretch 로 붙일 수 없는 경우(축이 마주보지 않음 / 직선 아님 / 너무 짧아짐)에는
                // 배관 길이를 그대로 두고, 다른 피팅류처럼 회전 + 이동으로 붙인다.
                // (Stretch 시도 중 문서가 바뀌었을 수 있으므로 커넥터를 다시 찾는다)
                LogUtils.Log("  Stretch 로 붙일 수 없어 회전 + 이동으로 붙입니다.");

                Connector refreshedMain = ElementUtils.ResolveConnector(doc, firstElem.Id, mainConn.Id);
                Connector refreshedSub = ElementUtils.ResolveConnector(doc, secondElem.Id, subConn.Id);
                if (refreshedMain == null || refreshedSub == null)
                {
                    LogUtils.Log("  커넥터를 다시 찾지 못해 연결하지 않습니다.");
                    return false;
                }

                ConnectorHelper.AlignAndConnect(doc, refreshedMain, refreshedSub, secondElem.Id);
                LogUtils.Log("===== 연결 종료. (배관 회전 + 이동) =====");
                return true;
            }

            // 배관이 아니면 회전 + 이동으로 붙인다.
            ConnectorHelper.AlignAndConnect(doc, mainConn, subConn, secondElem.Id);
            LogUtils.Log("===== 연결 종료. (회전 + 이동) =====");
            return true;
        }

        /// <summary>
        /// 두 객체의 열린 End 커넥터 조합 중 거리가 가장 가까운 한 쌍을 찾는다.
        /// 둘 중 하나라도 열린 커넥터가 없으면 false.
        /// </summary>
        public static bool FindNearestOpenConnectorPair(Element elemA, Element elemB,
            out Connector connA, out Connector connB)
        {
            connA = null;
            connB = null;

            List<Connector> connsA = ElementUtils.GetOpenEndConnectors(elemA);
            List<Connector> connsB = ElementUtils.GetOpenEndConnectors(elemB);
            if (connsA.Count == 0 || connsB.Count == 0) return false;

            double bestDist = double.MaxValue;

            foreach (Connector a in connsA)
            {
                foreach (Connector b in connsB)
                {
                    double dist = a.Origin.DistanceTo(b.Origin);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        connA = a;
                        connB = b;
                    }
                }
            }

            return connA != null && connB != null;
        }

        /// <summary>
        /// 배관의 끝(<paramref name="pipeConn"/> 쪽)을 늘리거나 줄여서 <paramref name="targetConn"/> 에 맞추고 연결한다.
        ///
        /// 처리 순서
        ///  1) 배관 축이 상대 커넥터와 마주보는지 검사. (아니면 Stretch 로는 붙일 수 없으므로 중단)
        ///  2) 상대 커넥터까지의 거리를 "축 방향" 과 "옆 방향" 으로 나눈다.
        ///  3) 옆 방향 어긋남만큼 배관 전체를 평행이동한다. (반대쪽 끝에 붙은 객체도 함께 밀린다)
        ///  4) 축 방향은 배관 끝점을 상대 커넥터 위치로 옮긴다. (= Stretch)
        ///  5) 두 커넥터를 연결한다.
        /// </summary>
        /// <returns>연결했으면 true. 배관이 직선이 아니거나 방향이 맞지 않으면 false.</returns>
        public static bool StretchPipeToConnector(Document doc, Pipe pipe, Connector pipeConn, Connector targetConn)
        {
            Line line = PipeGeometryUtils.GetPipeLine(pipe);
            if (line == null)
            {
                LogUtils.Log($"배관(Id={pipe.Id})이 직선이 아니라 Stretch 로 붙일 수 없습니다.");
                return false;
            }

            // 나중에 문서가 바뀌어도 쓸 수 있도록 좌표(XYZ)만 복사해 둔다.
            ElementId pipeId = pipe.Id;
            int pipeConnId = pipeConn.Id;
            ElementId targetOwnerId = targetConn.Owner.Id;
            int targetConnId = targetConn.Id;

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            XYZ pipeConnOrigin = pipeConn.Origin;

            // 옮길 끝(커넥터가 있는 쪽)과 고정할 끝을 정한다.
            bool moveStart = start.DistanceTo(pipeConnOrigin) <= end.DistanceTo(pipeConnOrigin);
            XYZ movingEnd = moveStart ? start : end;
            XYZ fixedEnd = moveStart ? end : start;

            // 배관 축: 고정 끝 → 옮길 끝 방향 (= 배관이 늘어나는 쪽)
            XYZ axis = (movingEnd - fixedEnd).Normalize();

            XYZ target = targetConn.Origin;
            XYZ targetDir = targetConn.CoordinateSystem.BasisZ.Normalize();   // 상대 커넥터가 바깥으로 향하는 방향

            // 1) 상대 커넥터는 배관 축의 반대 방향을 봐야 서로 마주본다.
            double facingAngle = axis.AngleTo(targetDir.Negate());
            if (facingAngle > FacingAngleTolerance)
            {
                LogUtils.Log($"배관(Id={pipeId}) 축과 상대 커넥터 방향이 마주보지 않아(약 {facingAngle * 180.0 / Math.PI:F1}도) " +
                    "Stretch 로 붙일 수 없습니다.");
                return false;
            }

            // 2) 상대 커넥터까지의 벡터를 축 방향 / 옆 방향으로 나눈다.
            XYZ delta = target - movingEnd;
            double alongLength = delta.DotProduct(axis);
            XYZ lateral = delta - axis * alongLength;

            // 3) 옆 어긋남만큼 배관 전체를 평행이동
            if (lateral.GetLength() > LateralTolerance)
            {
                ElementTransformUtils.MoveElement(doc, pipeId, lateral);
                fixedEnd = fixedEnd + lateral;
                LogUtils.Log($"  배관(Id={pipeId})을 옆으로 {FormatXyz(lateral)} 만큼 평행이동.");
            }

            // 4) 축 방향은 Stretch
            if (!PipeGeometryUtils.StretchPipeEndTo(doc, pipeId, movingEnd + lateral, target))
            {
                LogUtils.Log($"배관(Id={pipeId})의 끝을 {FormatXyz(target)} 로 옮기지 못했습니다. (너무 짧아짐)");
                return false;
            }

            doc.Regenerate();
            LogUtils.Log($"  배관(Id={pipeId}) 끝점을 {FormatXyz(target)} 로 Stretch.");

            // 5) 문서가 바뀌었으므로 커넥터를 다시 찾아서 연결
            Connector newPipeConn = ElementUtils.ResolveConnector(doc, pipeId, pipeConnId);
            Connector newTargetConn = ElementUtils.ResolveConnector(doc, targetOwnerId, targetConnId);

            if (newPipeConn == null || newTargetConn == null)
            {
                LogUtils.Log("Stretch 뒤 커넥터를 다시 찾지 못해 연결하지 못했습니다.");
                return false;
            }

            if (!newPipeConn.IsConnectedTo(newTargetConn))
                newPipeConn.ConnectTo(newTargetConn);

            return true;
        }

        private static string FormatXyz(XYZ p)
        {
            return p == null ? "null" : $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }
    }
}
