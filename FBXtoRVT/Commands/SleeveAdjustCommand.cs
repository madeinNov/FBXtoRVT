using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "타공 슬리브 조정" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SleeveAdjustCommand : IExternalCommand
    {
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
                // 2) 트랜잭션 안에서 실행 (플랜지 삭제 + 슬리브 이동/연결)
                SleeveAdjustHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "타공 슬리브 조정"))
                {
                    tx.Start();
                    runResult = SleeveAdjustHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                string summary =
                    $"타공 SLEEVE: {runResult.SleeveCount}개\n" +
                    $"삭제한 DC FLANGE: {runResult.DeletedFlangeCount}개\n\n" +
                    $"상부(Primary) 연결: {runResult.TopConnectedCount}건\n" +
                    $"하부 연결: {runResult.BottomConnectedCount}건\n" +
                    $"연결 실패: {runResult.FailedCount}건\n" +
                    $"대상 배관을 못 찾은 슬리브: {runResult.NoPipeSleeveCount}개";

                TaskDialog.Show("타공 슬리브 조정", summary);
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
