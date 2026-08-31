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
    ///
    /// 중심선 구하기 / 평행 판정은 <see cref="PipeGeometryUtils"/> 를 쓴다.
    /// ("직각 배관 연결기" 와 같은 기준으로 평행을 판정하기 위해서다)
    /// </summary>
    public static class DiagonalPipeHelper
    {
        // 두 중심선이 사실상 같은 선(수직 간격 0)인지 판정하는 거리 허용오차(feet).
        private const double CollinearTolerance = 1e-6;

        /// <summary>
        /// 대각 배관을 생성한다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        /// <returns>생성된 대각 배관</returns>
        public static Pipe CreateDiagonalPipe(Document doc, Pipe pipe1, Pipe pipe2)
        {
            Line line1 = PipeGeometryUtils.GetPipeLine(pipe1);
            Line line2 = PipeGeometryUtils.GetPipeLine(pipe2);
            if (line1 == null || line2 == null)
                throw new InvalidOperationException("직선 배관만 지원합니다.");

            if (!PipeGeometryUtils.AreParallel(line1, line2))
            {
                double offDegree = PipeGeometryUtils.AngleBetween(line1, line2) * 180.0 / Math.PI;
                throw new InvalidOperationException(
                    $"두 배관이 평행하지 않습니다. (약 {offDegree:F2}도 어긋남)\n평행한 두 배관을 선택하세요.");
            }

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
            // (파라미터가 없는 배관도 있으므로 반드시 null 을 확인한 뒤 읽는다)
            ElementId pipeTypeId = pipe1.GetTypeId();

            Parameter systemParam = pipe1.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId systemTypeId = (systemParam != null)
                ? systemParam.AsElementId()
                : ElementId.InvalidElementId;

            Parameter levelParam = pipe1.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            ElementId levelId = (levelParam != null)
                ? levelParam.AsElementId()
                : ElementId.InvalidElementId;

            // 기준 배관에 레벨 정보가 없으면 생성 위치에서 가장 가까운 레벨을 쓴다.
            if (levelId == ElementId.InvalidElementId)
                levelId = ElementUtils.FindNearestLevelId(doc, p1);

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
