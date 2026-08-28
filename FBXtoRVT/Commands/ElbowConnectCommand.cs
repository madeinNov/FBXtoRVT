using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "ELBOW&amp;배관/플랜지" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// 엘보의 열린 커넥터 주변 60mm 안의 FLANGE / 배관 끝점을 찾아 연결한다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElbowConnectCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "ELBOW&배관/플랜지";

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
            View activeView = doc.ActiveView;
            if (activeView == null)
            {
                message = "활성 뷰가 없습니다.";
                return Result.Failed;
            }

            try
            {
                // 2) 트랜잭션 안에서 실행 (플랜지/엘보 이동 + 연결)
                ElbowConnectHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "ELBOW 배관/플랜지 연결"))
                {
                    tx.Start();
                    runResult = ElbowConnectHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                string summary =
                    $"열린 커넥터가 있는 ELBOW: {runResult.ElbowCount}개\n\n" +
                    $"엘보에 붙인 FLANGE: {runResult.FlangeConnectedCount}건\n" +
                    $"배관 연결: {runResult.PipeConnectedCount}건\n" +
                    $"연결 실패: {runResult.FailedCount}건";

                TaskDialog.Show(FeatureTitle, summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
