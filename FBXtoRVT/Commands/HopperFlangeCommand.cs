using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "HOPPER&amp;플랜지" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// HOPPER 바운딩 박스(+50mm) 안에 FLANGE 가 딱 1개일 때, 파라미터를 정리하고 HOPPER 를 연결한다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class HopperFlangeCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "HOPPER&플랜지";

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
                // 2) 트랜잭션 안에서 실행 (파라미터 해제 + HOPPER 이동/연결)
                HopperFlangeHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "HOPPER 플랜지 연결"))
                {
                    tx.Start();
                    runResult = HopperFlangeHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                string summary =
                    $"HOPPER: {runResult.HopperCount}개\n" +
                    $"연결 대상 FLANGE: {runResult.TargetFlangeCount}개\n" +
                    $"박스 안 FLANGE 가 1개가 아니라 건너뜀: {runResult.SkippedCount}개\n\n" +
                    $"ND1 복사: {runResult.Nd1CopiedCount}건\n" +
                    $"ND1 미적용(HOPPER 커넥터 ND 불일치): {runResult.Nd1SkippedMixedCount}건\n" +
                    $"파라미터 해제: {runResult.ParamUncheckedCount}건\n" +
                    $"연결 성공: {runResult.ConnectedCount}건\n" +
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
