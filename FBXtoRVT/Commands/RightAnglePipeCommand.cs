// ================================================================
// "직각 배관 생성기" 기능은 사용하지 않기로 하여 전체를 주석 처리했습니다.
// 리본 버튼(아이콘)도 App.cs 에서 제거했습니다.
// 되살리려면 아래 /* ... */ 주석을 풀고, App.cs 의 "공용" 패널에
// AddButton 호출을 다시 넣으면 됩니다.
// ================================================================
/*
using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "직각 배관 생성기" 버튼이 실행하는 명령.
    ///
    /// 흐름
    ///   1) 이어줄 배관들을 한 번에 선택 (여러 개 가능, 배관만)
    ///   2) 선택한 배관들끼리 직각 + 서로 만나지 않는 쌍을 찾아 "사잇배관" 생성
    ///
    /// 짝짓기 규칙
    ///   - 조건을 만족하는 조합 중 서로 가장 가까운 쌍부터 배정하고,
    ///     한 번 짝지어진 배관은 다시 쓰지 않는다.
    ///   - 기준 배관(타입 / 지름 / System Type 출처)은 쌍에서 중심점 Z 가 더 높은 배관이다.
    ///     Z 까지 같으면 어느 쪽이든 상관없으므로 Element Id 가 작은 쪽으로 고정한다.
    ///
    /// 사잇배관의 양 끝점은 두 배관 중심선의 연장선 위에 정확히 놓이므로,
    /// 이후 유저가 Trim 으로 Elbow 를 넣을 수 있다. (Elbow 삽입은 이 기능에서 하지 않는다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RightAnglePipeCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "직각 배관 생성기";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

            // 1) 열린 문서 확인
            if (uiDoc == null || uiDoc.Document == null)
            {
                message = "열린 문서가 없습니다.";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;

            try
            {
                // 2) 이어줄 배관들을 한 번에 선택
                IList<Reference> pipeRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element, new PipeSelectionFilter(),
                    "직각으로 이어줄 배관들을 선택하세요. (여러 개 가능 · 완료는 Finish)");

                List<Pipe> pipes = ToPipes(doc, pipeRefs);

                if (pipes.Count < 2)
                {
                    TaskDialog.Show(FeatureTitle, "배관을 2개 이상 선택하세요.");
                    return Result.Cancelled;
                }

                // 3) 트랜잭션 안에서 사잇배관 생성
                RightAnglePipeHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "직각 사잇배관 생성"))
                {
                    tx.Start();
                    runResult = RightAnglePipeHelper.Run(doc, pipes);
                    tx.Commit();
                }

                // 4) 만들어진 배관을 선택 상태로 만들어 바로 확인할 수 있게 한다.
                if (runResult.CreatedCount > 0)
                {
                    uiDoc.Selection.SetElementIds(runResult.CreatedPipeIds);
                }

                // 5) 결과 요약 표시
                TaskDialog.Show(FeatureTitle, BuildSummary(pipes.Count, runResult));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 사용자가 ESC 로 취소
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "직각 배관 생성기 실행 실패");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// 선택 결과(Reference)를 배관 목록으로 바꾼다. 배관이 아닌 것은 버린다.
        /// </summary>
        private static List<Pipe> ToPipes(Document doc, IList<Reference> refs)
        {
            var pipes = new List<Pipe>();
            if (refs == null) return pipes;

            foreach (Reference r in refs)
            {
                Pipe pipe = doc.GetElement(r) as Pipe;
                if (pipe != null) pipes.Add(pipe);
            }

            return pipes;
        }

        /// <summary>
        /// 결과 요약 문구를 만든다. 짝을 못 지은 배관은 이유별로 나눠서 보여준다.
        /// </summary>
        private static string BuildSummary(int selectedCount, RightAnglePipeHelper.RunResult r)
        {
            string summary =
                $"선택한 배관: {selectedCount}개\n" +
                $"생성한 사잇배관: {r.CreatedCount}개\n";

            // 건너뛴 사유가 하나라도 있으면 이어서 표시
            var reasons = new List<string>();

            if (r.NotStraightCount > 0)
                reasons.Add($"직선이 아니라 제외: {r.NotStraightCount}개");

            if (r.AlreadyIntersectCount > 0)
                reasons.Add($"이미 직각으로 만남(사잇배관 불필요, Trim 만 하면 됨): {r.AlreadyIntersectCount}개");

            if (r.NoPerpendicularCount > 0)
                reasons.Add($"직각인 상대 배관이 없음: {r.NoPerpendicularCount}개");

            if (r.PartnerTakenCount > 0)
                reasons.Add($"상대 배관이 다른 쌍에 먼저 배정됨: {r.PartnerTakenCount}개");

            if (r.CreateFailedCount > 0)
                reasons.Add($"배관 생성 실패: {r.CreateFailedCount}건");

            if (reasons.Count > 0)
            {
                summary += "\n[짝을 짓지 못한 배관]\n";
                foreach (string reason in reasons)
                {
                    summary += "· " + reason + "\n";
                }
            }

            return summary;
        }
    }
}
*/
