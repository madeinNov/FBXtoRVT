using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "직각 배관 연결기" 기능의 핵심 로직.
    ///
    /// [무엇을 하는 기능인가]
    /// <b>평행한</b> 두 배관을 차례로 클릭하면, 둘을 잇는 "직각 배관" 을 자동으로 만들고
    /// 엘보까지 넣어 <b>세 배관을 하나로 연결</b>한다.
    /// (유저가 손으로 배관을 그리고 Trim 을 두 번 하던 작업을 한 번에 처리한다)
    ///
    /// "대각 배관 생성기"(<see cref="DiagonalPipeHelper"/>)와 짝을 이루는 기능이다.
    ///   - 대각 배관 생성기 : 평행한 두 배관 사이에 <b>45도</b> 배관을 만든다. (연결은 유저가 Trim)
    ///   - 직각 배관 연결기 : 평행한 두 배관 사이에 <b>90도</b> 배관을 만들고 연결까지 한다.
    ///
    /// [처리 순서]
    ///  1) 두 배관의 중심선을 구한다. (직선이 아니거나 평행이 아니면 안내하고 중단)
    ///  2) 두 배관에서 서로 마주보는 쪽 끝점(= 가까운 커넥터)을 찾는다.
    ///  3) 그 두 끝점의 <b>축 방향 가운데</b>에 직각 배관을 세우기로 정하고,
    ///     각 중심선 위에서 그 위치의 점 P1 / P2 를 구한다.
    ///     P1 ~ P2 가 곧 두 중심선의 공통수선이다. (축에 정확히 수직)
    ///  4) 이어 붙일 커넥터에 이미 붙어 있는 객체(캡 / 플랜지 / 기존 엘보)가 있으면 지운다.
    ///  5) 각 배관의 끝을 P1 / P2 까지 늘리거나 줄인다. (= 유저가 하던 Trim)
    ///  6) P1 ~ P2 를 잇는 직각 배관을 만든다.
    ///  7) 양쪽에 엘보를 넣어 세 배관의 연결을 마무리한다.
    ///
    /// [기준 배관]
    /// 직각 배관의 배관 타입 / System Type / 지름 / 레벨은 <b>첫 번째로 고른 배관</b>을 따라간다.
    /// </summary>
    public static class RightAngleConnectHelper
    {
        /// <summary>
        /// 실행 결과 요약. Commands 쪽에서 대화상자로 보여준다.
        /// </summary>
        public class ConnectResult
        {
            /// <summary>새로 만든 직각 배관 Id</summary>
            public ElementId RightAnglePipeId;

            /// <summary>두 중심선 사이 거리 = 직각 배관의 길이(feet)</summary>
            public double PipeLength;

            /// <summary>커넥터에 붙어 있어서 지운 객체 수</summary>
            public int RemovedElementCount;

            /// <summary>실제로 넣은 엘보 수 (정상이면 2)</summary>
            public int ElbowCount;

            /// <summary>넣으려고 했지만 실패한 엘보 수</summary>
            public int ElbowFailedCount;
        }

        /// <summary>
        /// 평행한 두 배관을 직각 배관으로 연결한다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        /// <param name="firstPipe">먼저 고른 배관. 타입 / 지름 / System Type 의 기준이 된다.</param>
        /// <param name="secondPipe">나중에 고른 배관</param>
        public static ConnectResult Connect(Document doc, Pipe firstPipe, Pipe secondPipe)
        {
            var result = new ConnectResult();

            // 배관을 만들 수 있는 최소 길이. 이보다 짧으면 Revit 이 배관을 만들지 못한다.
            double minLength = doc.Application.ShortCurveTolerance;

            // 1) 두 배관의 중심선
            Line line1 = PipeGeometryUtils.GetPipeLine(firstPipe);
            Line line2 = PipeGeometryUtils.GetPipeLine(secondPipe);

            if (line1 == null) throw new InvalidOperationException("첫 번째 배관이 직선이 아니라 연결할 수 없습니다.");
            if (line2 == null) throw new InvalidOperationException("두 번째 배관이 직선이 아니라 연결할 수 없습니다.");

            if (!PipeGeometryUtils.AreParallel(line1, line2))
            {
                double offDegree = PipeGeometryUtils.AngleBetween(line1, line2) * 180.0 / Math.PI;
                throw new InvalidOperationException(
                    $"두 배관이 평행하지 않습니다. (약 {offDegree:F2}도 어긋남)\n평행한 두 배관을 고르세요.");
            }

            // 기준 축 = 첫 배관의 방향. 직각 배관은 이 축에 정확히 수직으로 만든다.
            XYZ axis = line1.Direction.Normalize();

            // 2) 서로 마주보는 쪽 끝점 = 이번에 이어 붙일 커넥터의 위치
            XYZ near1, near2;
            PipeGeometryUtils.FindNearestEndPointPair(line1, line2, out near1, out near2);

            // 3) 두 끝점의 축 방향 가운데에 직각 배관을 세운다.
            //    (마주보고 떨어져 있으면 그 사이에, 겹쳐 있으면 겹친 자리에 놓이게 된다)
            double station =
                (PipeGeometryUtils.GetStation(near1, axis) + PipeGeometryUtils.GetStation(near2, axis)) * 0.5;

            XYZ p1, p2;
            if (!PipeGeometryUtils.TryGetPointAtStation(line1, axis, station, out p1)
                || !PipeGeometryUtils.TryGetPointAtStation(line2, axis, station, out p2))
            {
                throw new InvalidOperationException("두 배관의 위치로는 직각 배관을 놓을 자리를 정할 수 없습니다.");
            }

            result.PipeLength = p1.DistanceTo(p2);

            if (result.PipeLength < minLength)
                throw new InvalidOperationException(
                    "두 배관이 같은 직선 위에 있어 직각 배관을 만들 수 없습니다.\n" +
                    "나란히 떨어져 있는 두 배관을 고르세요.");

            LogUtils.Log($"===== 직각 배관 연결기 시작. 첫배관 Id={firstPipe.Id} 둘째배관 Id={secondPipe.Id} " +
                $"P1={LogUtils.FormatXyz(p1)} P2={LogUtils.FormatXyz(p2)} 길이={result.PipeLength:F4}ft =====");

            ElementId firstId = firstPipe.Id;
            ElementId secondId = secondPipe.Id;

            // 4) 이어 붙일 커넥터에 이미 붙어 있는 객체를 지운다.
            //    (아직 배관을 옮기기 전이므로, 커넥터는 원래 끝점 near1 / near2 자리에 있다)
            result.RemovedElementCount += RemoveAttachedElements(doc, firstId, near1, secondId);
            result.RemovedElementCount += RemoveAttachedElements(doc, secondId, near2, firstId);
            doc.Regenerate();

            // 5) 각 배관의 끝을 P1 / P2 까지 늘리거나 줄인다. (= Trim)
            StretchPipeEndTo(doc, firstId, near1, p1, minLength, "첫 번째 배관");
            StretchPipeEndTo(doc, secondId, near2, p2, minLength, "두 번째 배관");
            doc.Regenerate();

            // 6) 직각 배관 생성
            Pipe rightAnglePipe = CreateRightAnglePipe(doc, firstId, p1, p2);
            result.RightAnglePipeId = rightAnglePipe.Id;
            doc.Regenerate();

            // 7) 엘보 삽입 (첫 배관 ↔ 직각 배관 ↔ 둘째 배관)
            AddElbow(doc, firstId, p1, rightAnglePipe.Id, p1, result);
            doc.Regenerate();

            // 앞의 엘보 때문에 직각 배관이 짧아졌을 수 있으므로 커넥터를 다시 찾는다.
            AddElbow(doc, rightAnglePipe.Id, p2, secondId, p2, result);

            LogUtils.Log($"===== 직각 배관 연결기 종료. 직각배관 Id={result.RightAnglePipeId} " +
                $"지운객체={result.RemovedElementCount} 엘보성공={result.ElbowCount} 엘보실패={result.ElbowFailedCount} =====");

            return result;
        }

        // ===== 4) 커넥터에 붙어 있는 객체 제거 =====

        /// <summary>
        /// 배관의 지정한 끝 커넥터에 붙어 있는 객체를 지운다.
        ///
        /// 캡 / 플랜지 / 기존 엘보처럼 <b>부품(FamilyInstance)</b> 만 지운다.
        /// 다른 배관이 이어져 있는 경우에는 지우지 않고 연결만 끊는다.
        /// (배관을 통째로 지우면 유저가 의도하지 않은 구간까지 사라지기 때문)
        /// </summary>
        /// <param name="nearPoint">이어 붙일 쪽 끝점. 이 점에 가장 가까운 커넥터를 본다.</param>
        /// <param name="keepId">이번에 이어줄 상대 배관. 절대 지우지 않는다.</param>
        /// <returns>지운 객체 수</returns>
        private static int RemoveAttachedElements(Document doc, ElementId pipeId, XYZ nearPoint, ElementId keepId)
        {
            Element pipe = doc.GetElement(pipeId);
            if (pipe == null) return 0;

            Connector conn = ElementUtils.FindNearestEndConnector(pipe, nearPoint);
            if (conn == null || !conn.IsConnected) return 0;

            // 지울 대상과 연결만 끊을 대상을 먼저 모은다.
            // (돌면서 지우면 ConnectorSet 이 도중에 바뀌므로 반드시 모아 두고 처리한다)
            var toDelete = new List<ElementId>();
            var toDisconnect = new List<Connector>();

            foreach (Connector other in conn.AllRefs)
            {
                if (other.ConnectorType != ConnectorType.End) continue;   // 논리적(System) 참조는 제외

                Element owner = other.Owner;
                if (owner == null) continue;
                if (owner.Id == pipeId) continue;      // 자기 자신
                if (owner.Id == keepId) continue;      // 이번에 이어줄 상대 배관은 건드리지 않는다

                if (owner is FamilyInstance)
                {
                    if (!toDelete.Contains(owner.Id)) toDelete.Add(owner.Id);
                    LogUtils.LogDetail($"  배관(Id={pipeId}) 커넥터에 붙어 있던 부품(Id={owner.Id}) 제거 예정.");
                }
                else
                {
                    toDisconnect.Add(other);
                    LogUtils.LogDetail($"  배관(Id={pipeId}) 커넥터에 이어져 있던 객체(Id={owner.Id})는 연결만 끊음.");
                }
            }

            foreach (Connector other in toDisconnect)
            {
                try
                {
                    if (conn.IsConnectedTo(other)) conn.DisconnectFrom(other);
                }
                catch (Exception ex)
                {
                    LogUtils.LogError(ex, $"배관(Id={pipeId}) 커넥터 연결 끊기 실패.");
                }
            }

            if (toDelete.Count > 0) doc.Delete(toDelete);

            return toDelete.Count;
        }

        // ===== 5) 배관 끝점 이동 =====

        /// <summary>
        /// 배관의 두 끝점 중 <paramref name="endToMove"/> 쪽 끝을 목표점으로 옮긴다.
        /// (유저가 손으로 하던 Trim / Extend 와 같은 결과)
        /// 반대쪽 끝은 그대로 두므로, 그쪽에 연결된 객체는 영향을 받지 않는다.
        /// </summary>
        private static void StretchPipeEndTo(Document doc, ElementId pipeId, XYZ endToMove, XYZ targetPoint,
            double minLength, string pipeLabel)
        {
            var pipe = doc.GetElement(pipeId) as Pipe;
            if (pipe == null) throw new InvalidOperationException($"{pipeLabel} 을(를) 찾지 못했습니다.");

            var lc = pipe.Location as LocationCurve;
            Line line = (lc != null) ? lc.Curve as Line : null;
            if (line == null) throw new InvalidOperationException($"{pipeLabel} 이(가) 직선이 아니라 연결할 수 없습니다.");

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);

            // 옮길 끝이 시작점 쪽인지 끝점 쪽인지 판정
            bool moveStart = start.DistanceTo(endToMove) <= end.DistanceTo(endToMove);
            XYZ movingEnd = moveStart ? start : end;
            XYZ fixedEnd = moveStart ? end : start;

            // 이미 목표점에 있으면 건드리지 않는다.
            if (movingEnd.DistanceTo(targetPoint) < minLength) return;

            if (fixedEnd.DistanceTo(targetPoint) < minLength)
                throw new InvalidOperationException(
                    $"{pipeLabel} 이(가) 너무 짧아져서 연결할 수 없습니다. 다른 위치의 배관을 고르세요.");

            lc.Curve = moveStart
                ? Line.CreateBound(targetPoint, fixedEnd)
                : Line.CreateBound(fixedEnd, targetPoint);

            LogUtils.LogDetail($"  {pipeLabel}(Id={pipeId}) 끝점을 {LogUtils.FormatXyz(targetPoint)} 로 맞춤.");
        }

        // ===== 6) 직각 배관 생성 =====

        /// <summary>
        /// 공통수선의 두 점을 잇는 직각 배관을 만든다.
        /// 타입 / System Type / 지름 / 레벨은 첫 번째로 고른 배관을 따라간다.
        /// </summary>
        private static Pipe CreateRightAnglePipe(Document doc, ElementId firstPipeId, XYZ from, XYZ to)
        {
            var basePipe = doc.GetElement(firstPipeId) as Pipe;
            if (basePipe == null) throw new InvalidOperationException("첫 번째 배관을 찾지 못했습니다.");

            ElementId pipeTypeId = basePipe.GetTypeId();

            Parameter systemParam = basePipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId systemTypeId = (systemParam != null)
                ? systemParam.AsElementId()
                : ElementId.InvalidElementId;

            Parameter levelParam = basePipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            ElementId levelId = (levelParam != null)
                ? levelParam.AsElementId()
                : ElementId.InvalidElementId;

            // 기준 배관에 레벨 정보가 없으면 생성 위치에서 가장 가까운 레벨을 쓴다.
            if (levelId == ElementId.InvalidElementId)
                levelId = ElementUtils.FindNearestLevelId(doc, from);

            double baseDiameter = basePipe.Diameter;

            Pipe newPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, from, to);

            // 지름도 첫 배관과 동일하게 맞춘다.
            Parameter diaParam = newPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
            {
                diaParam.Set(baseDiameter);
            }

            LogUtils.LogDetail($"  직각 배관 생성 Id={newPipe.Id} {LogUtils.FormatXyz(from)} -> {LogUtils.FormatXyz(to)}");
            return newPipe;
        }

        // ===== 7) 엘보 삽입 =====

        /// <summary>
        /// 두 객체의 커넥터 사이에 엘보를 넣는다.
        /// 이미 연결돼 있으면 아무것도 하지 않고, 실패하면 사유를 기록만 하고 넘어간다.
        /// (지름이 서로 다르면 Revit 이 엘보를 만들지 못할 수 있다)
        /// </summary>
        private static void AddElbow(Document doc, ElementId ownerAId, XYZ nearPointA,
            ElementId ownerBId, XYZ nearPointB, ConnectResult result)
        {
            Element ownerA = doc.GetElement(ownerAId);
            Element ownerB = doc.GetElement(ownerBId);
            if (ownerA == null || ownerB == null)
            {
                result.ElbowFailedCount++;
                return;
            }

            Connector connA = ElementUtils.FindNearestEndConnector(ownerA, nearPointA);
            Connector connB = ElementUtils.FindNearestEndConnector(ownerB, nearPointB);

            if (connA == null || connB == null)
            {
                LogUtils.Log($"  엘보 삽입 실패: 커넥터를 찾지 못함. A={ownerAId} B={ownerBId}");
                result.ElbowFailedCount++;
                return;
            }

            if (connA.IsConnectedTo(connB))
            {
                LogUtils.LogDetail($"  이미 연결돼 있어 엘보를 넣지 않음. A={ownerAId} B={ownerBId}");
                return;
            }

            try
            {
                doc.Create.NewElbowFitting(connA, connB);
                result.ElbowCount++;
                LogUtils.LogDetail($"  엘보 삽입 성공. A={ownerAId} B={ownerBId}");
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"엘보 삽입 실패. A={ownerAId} B={ownerBId}");
                result.ElbowFailedCount++;
            }
        }
    }
}
