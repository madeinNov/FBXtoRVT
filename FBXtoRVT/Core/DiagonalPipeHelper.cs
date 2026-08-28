using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 대각 배관 생성기의 핵심 로직.
    /// 평행한 두 배관 사이에, 배관 방향으로부터 45도인 대각 배관을 생성한다.
    /// 대각 배관의 양 끝점은 두 배관 중심선의 연장선 위에 놓여(=trim 가능),
    /// 두 중심점을 고려해 한 방향으로만 진행(지그재그 방지)하도록 배치한다.
    /// </summary>
    public static class DiagonalPipeHelper
    {
        // 평행 판정 각도 허용오차(라디안). 약 1도.
        private const double ParallelAngleTolerance = 0.0175;

        // 두 중심선이 사실상 같은 선(수직 간격 0)인지 판정하는 거리 허용오차(feet).
        private const double CollinearTolerance = 1e-6;

        /// <summary>
        /// 배관 하나의 중심선(Line)을 반환. 직선이 아니면 null.
        /// </summary>
        public static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc != null ? lc.Curve as Line : null;
        }

        /// <summary>
        /// 두 배관이 평행한지 검사(같은 방향이든 반대 방향이든 평행이면 true).
        /// </summary>
        public static bool AreParallel(Line line1, Line line2)
        {
            XYZ d1 = line1.Direction;
            XYZ d2 = line2.Direction;
            double angle = d1.AngleTo(d2); // 0 ~ PI
            return angle < ParallelAngleTolerance || angle > Math.PI - ParallelAngleTolerance;
        }

        /// <summary>
        /// 대각 배관을 생성한다. (외부에서 Transaction 을 열고 호출)
        /// </summary>
        /// <returns>생성된 대각 배관</returns>
        public static Pipe CreateDiagonalPipe(Document doc, Pipe pipe1, Pipe pipe2)
        {
            Line line1 = GetPipeLine(pipe1);
            Line line2 = GetPipeLine(pipe2);
            if (line1 == null || line2 == null)
                throw new InvalidOperationException("직선 배관만 지원합니다.");

            if (!AreParallel(line1, line2))
                throw new InvalidOperationException("두 배관이 평행하지 않습니다. 평행한 두 배관을 선택하세요.");

            // 첫 배관 방향을 기준축 u 로 사용
            XYZ u = line1.Direction.Normalize();

            // 각 배관의 중심점
            XYZ c1 = (line1.GetEndPoint(0) + line1.GetEndPoint(1)) * 0.5;
            XYZ c2 = (line2.GetEndPoint(0) + line2.GetEndPoint(1)) * 0.5;

            // 두 중심점 차이를 축 성분(g)과 수직 성분(w)으로 분해
            XYZ diff = c2 - c1;
            double g = diff.DotProduct(u);      // 축 방향 차이
            XYZ w = diff - g * u;               // 중심선 L1 → L2 수직 오프셋
            double d = w.GetLength();           // 두 중심선 사이 거리

            if (d < CollinearTolerance)
                throw new InvalidOperationException("두 배관이 같은 직선상에 있어 대각 배관을 만들 수 없습니다.");

            // 지그재그 방지: 축 방향 진행 부호를 g 의 부호와 같게 (g==0 이면 +방향 기본)
            double sign = (g >= 0.0) ? 1.0 : -1.0;

            // 45도 조건에서 축 방향 이동량 = 수직 간격 d.
            // 대각 배관을 두 중심점의 중간 지점에 배치.
            // P1 은 L1(첫 배관 중심선) 위, P2 는 L2(둘째 배관 중심선) 위에 놓임.
            XYZ p1 = c1 + ((g - sign * d) / 2.0) * u;
            XYZ p2 = c1 + w + ((g + sign * d) / 2.0) * u;

            // 첫 배관과 동일한 타입/시스템/레벨/지름으로 생성
            ElementId pipeTypeId = pipe1.GetTypeId();
            ElementId systemTypeId = pipe1.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
            ElementId levelId = pipe1.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsElementId();

            Pipe newPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p1, p2);

            // 지름을 첫 배관과 동일하게 설정
            Parameter diaParam = newPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
            {
                diaParam.Set(pipe1.Diameter);
            }

            return newPipe;
        }
    }
}
