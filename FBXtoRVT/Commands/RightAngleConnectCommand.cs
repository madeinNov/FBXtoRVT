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
    /// "직각 배관 연결기" 버튼이 실행하는 명령.
    ///
    /// 흐름
    ///   1) 첫 번째 배관 클릭 (타입 / 지름 / System Type 의 기준)
    ///   2) 두 번째 배관 클릭
    ///   3) 평행한 두 배관 사이에 직각 배관을 만들고, 엘보까지 넣어 세 배관을 연결한다.
    ///
    /// "대각 배관 생성기" 가 45도 배관을 만들어 주는 것과 짝을 이루는 기능이며,
    /// 이쪽은 90도 배관을 만들고 연결(Trim + 엘보)까지 끝낸다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RightAngleConnectCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "직각 배관 연결기";

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
                var filter = new PipeSelectionFilter();

                // 2) 첫 번째 배관 클릭 (타입 / 지름 / System Type 의 기준이 된다)
                Reference ref1 = uiDoc.Selection.PickObject(
                    ObjectType.Element, filter, "첫 번째 배관을 클릭하세요. (타입·지름의 기준)");
                Pipe pipe1 = doc.GetElement(ref1) as Pipe;

                // 3) 두 번째 배관 클릭 (첫 배관과 평행해야 한다)
                Reference ref2 = uiDoc.Selection.PickObject(
                    ObjectType.Element, filter, "두 번째 배관을 클릭하세요. (첫 배관과 평행한 배관)");
                Pipe pipe2 = doc.GetElement(ref2) as Pipe;

                if (pipe1 == null || pipe2 == null)
                {
                    message = "배관 선택에 실패했습니다.";
                    return Result.Failed;
                }

                if (pipe1.Id == pipe2.Id)
                {
                    TaskDialog.Show(FeatureTitle, "서로 다른 두 배관을 선택하세요.");
                    return Result.Cancelled;
                }

                // 4) 트랜잭션 안에서 연결 (부품 제거 + 배관 끝점 이동 + 직각 배관 + 엘보)
                RightAngleConnectHelper.ConnectResult runResult;

                using (Transaction tx = new Transaction(doc, "직각 배관 연결"))
                {
                    tx.Start();
                    runResult = RightAngleConnectHelper.Connect(doc, pipe1, pipe2);
                    tx.Commit();
                }

                // 5) 만들어진 직각 배관을 선택 상태로 만들어 바로 확인할 수 있게 한다.
                if (runResult.RightAnglePipeId != null)
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId> { runResult.RightAnglePipeId });
                }

                // 6) 결과 요약 표시 (문제없이 끝났으면 조용히 넘어간다)
                string summary = BuildSummary(runResult);
                if (summary != null) TaskDialog.Show(FeatureTitle, summary);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 사용자가 ESC 로 취소
                return Result.Cancelled;
            }
            catch (InvalidOperationException ex)
            {
                // 직각이 아님 / 직선이 아님 등 조건 불충족: 안내 후 종료 (트랜잭션은 롤백됨)
                TaskDialog.Show(FeatureTitle, ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "직각 배관 연결기 실행 실패");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// 결과 요약 문구를 만든다.
        /// 직각 배관 + 엘보 2개가 모두 정상이면 알려줄 것이 없으므로 null 을 돌려 창을 띄우지 않는다.
        /// </summary>
        private static string BuildSummary(RightAngleConnectHelper.ConnectResult r)
        {
            // 첫 배관 ↔ 직각 배관 ↔ 둘째 배관 이므로 엘보는 2개가 정상이다.
            const int ExpectedElbowCount = 2;

            bool allFine = r.ElbowFailedCount == 0 && r.ElbowCount == ExpectedElbowCount;
            if (allFine && r.RemovedElementCount == 0) return null;

            string summary = "직각 배관을 만들어 두 배관을 이었습니다.\n\n" +
                             $"넣은 엘보: {r.ElbowCount}개 / {ExpectedElbowCount}개\n";

            if (r.RemovedElementCount > 0)
                summary += $"커넥터에 붙어 있어서 지운 객체: {r.RemovedElementCount}개\n";

            if (r.ElbowFailedCount > 0)
            {
                summary += $"\n[엘보를 넣지 못함: {r.ElbowFailedCount}개]\n" +
                           "두 배관의 지름이 서로 다르면 엘보가 만들어지지 않습니다.\n" +
                           "지름을 맞춘 뒤 Trim 으로 직접 넣어 주세요.";
            }

            return summary;
        }
    }
}
