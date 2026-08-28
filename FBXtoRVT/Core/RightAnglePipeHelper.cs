using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "직각 배관 생성기" 기능의 핵심 로직.
    ///
    /// 사용자가 배관(첫 객체)과 아무 객체(둘째 객체)를 차례로 고르면,
    /// 첫 배관의 중심선을 무한히 연장한 직선에 둘째 객체의 기준점에서 수선의 발을 내리고,
    /// "수선의 발 ~ 둘째 객체 기준점" 을 잇는 배관을 새로 만든다.
    /// (첫 배관의 중심선과 직각으로 만나므로 '직각 배관')
    ///
    /// 둘째 객체의 기준점은 종류에 따라 다르다.
    ///  - 배관인 경우      : 사용할 커넥터의 원점 (아래 ChooseTargetPipeConnector 규칙)
    ///  - 배관이 아닌 경우 : 객체의 중심점(바운딩 박스 중앙)
    ///
    /// 새로 만든 배관의 배관 타입 / System Type / 지름은 첫 배관과 동일하게 맞춘다.
    /// </summary>
    public static class RightAnglePipeHelper
    {
        // 수선의 발과 기준점이 사실상 같은 점인지(= 길이 0 배관) 판정하는 허용오차(feet).
        // 약 0.003mm 로, 두 점이 겹칠 때만 걸린다.
        private const double MinLengthTolerance = 1e-5;

        /// <summary>
        /// 배관 하나의 중심선(Line)을 반환. 직선이 아니면 null.
        /// </summary>
        public static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc != null ? lc.Curve as Line : null;
        }

        /// <summary>
        /// 직각 배관을 생성한다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        /// <param name="basePipe">기준이 되는 첫 배관 (중심선을 연장해 직선으로 사용)</param>
        /// <param name="target">두 번째로 고른 객체 (배관이거나, 그 밖의 아무 객체)</param>
        /// <returns>생성된 배관</returns>
        public static Pipe CreateRightAnglePipe(Document doc, Pipe basePipe, Element target)
        {
            Line baseLine = GetPipeLine(basePipe);
            if (baseLine == null)
                throw new InvalidOperationException("첫 번째 객체는 직선 배관이어야 합니다.");

            // 1) 둘째 객체의 기준점을 정한다.
            XYZ targetPoint = GetTargetPoint(basePipe, baseLine, target);

            // 2) 첫 배관 중심선을 무한히 연장한 직선에 수선의 발을 내린다.
            XYZ foot = ProjectOntoLine(baseLine, targetPoint);

            // 3) 두 점이 겹치면(= 기준점이 이미 직선 위에 있으면) 배관을 만들 수 없다.
            if (foot.DistanceTo(targetPoint) < MinLengthTolerance)
                throw new InvalidOperationException(
                    "두 번째 객체의 기준점이 첫 배관의 연장선 위에 있어, 직각 배관을 만들 수 없습니다.");

            // 4) 첫 배관과 동일한 타입 / System Type / 레벨로 생성
            ElementId pipeTypeId = basePipe.GetTypeId();
            ElementId systemTypeId = basePipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
            ElementId levelId = basePipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsElementId();

            Pipe newPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, foot, targetPoint);

            // 지름도 첫 배관과 동일하게 맞춘다.
            Parameter diaParam = newPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
            {
                diaParam.Set(basePipe.Diameter);
            }

            return newPipe;
        }

        /// <summary>
        /// 둘째 객체에서 "수선의 발을 내릴 기준점" 을 구한다.
        ///  - 배관이면 : 사용할 커넥터의 원점
        ///  - 아니면   : 객체 중심점(바운딩 박스 중앙)
        /// </summary>
        private static XYZ GetTargetPoint(Pipe basePipe, Line baseLine, Element target)
        {
            var targetPipe = target as Pipe;

            if (targetPipe != null)
            {
                Connector chosen = ChooseTargetPipeConnector(basePipe, baseLine, targetPipe);
                return chosen.Origin;
            }

            XYZ center = ElementUtils.GetCenter(target);
            if (center == null)
                throw new InvalidOperationException("두 번째 객체의 중심점을 구하지 못했습니다.");

            return center;
        }

        /// <summary>
        /// 둘째 배관에서 사용할 커넥터를 고른다.
        ///  - 닫힌(이미 연결된) 커넥터가 2개면 : 작업 중단 (예외)
        ///  - 닫힌 커넥터가 1개면              : 무조건 남은 열린 커넥터를 사용
        ///  - 둘 다 열려 있으면                : 첫 배관의 양 끝점에 더 가까운 쪽을 사용
        /// </summary>
        private static Connector ChooseTargetPipeConnector(Pipe basePipe, Line baseLine, Pipe targetPipe)
        {
            List<Connector> endConns = ElementUtils.GetEndConnectors(targetPipe);
            if (endConns.Count == 0)
                throw new InvalidOperationException("두 번째 배관에서 커넥터를 찾지 못했습니다.");

            // 열린 커넥터 / 닫힌 커넥터 분류
            var openConns = new List<Connector>();
            int closedCount = 0;

            foreach (Connector c in endConns)
            {
                if (c.IsConnected) closedCount++;
                else openConns.Add(c);
            }

            if (closedCount >= 2)
                throw new InvalidOperationException(
                    "두 번째 배관의 커넥터가 2개 모두 이미 연결되어 있습니다.\n" +
                    "열린 커넥터가 있는 배관을 선택하세요.");

            if (openConns.Count == 0)
                throw new InvalidOperationException("두 번째 배관에 열린 커넥터가 없습니다.");

            // 닫힌 커넥터가 1개면 남은 열린 커넥터가 곧 정답
            if (openConns.Count == 1)
                return openConns[0];

            // 둘 다 열려 있으면 첫 배관의 양 끝점에 더 가까운 커넥터를 고른다.
            XYZ baseStart = baseLine.GetEndPoint(0);
            XYZ baseEnd = baseLine.GetEndPoint(1);

            Connector best = null;
            double bestDist = double.MaxValue;

            foreach (Connector c in openConns)
            {
                // 첫 배관 양 끝점 중 가까운 쪽까지의 거리
                double dist = Math.Min(c.Origin.DistanceTo(baseStart), c.Origin.DistanceTo(baseEnd));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// 직선(line 을 무한히 연장한 것) 위로 점의 수선의 발을 구한다.
        /// </summary>
        private static XYZ ProjectOntoLine(Line line, XYZ point)
        {
            XYZ origin = line.GetEndPoint(0);
            XYZ dir = line.Direction.Normalize();

            // (point - origin) 을 방향벡터에 정사영한 길이만큼 원점에서 이동
            double t = (point - origin).DotProduct(dir);
            return origin + t * dir;
        }
    }
}
