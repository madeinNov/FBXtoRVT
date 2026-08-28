using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "장비&amp;플랜지/NUT" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// ScrubberFlangeCommand 와 동일하되, 대상이 'SCRUBBER' 가 아니라 Mechanical Equipment 전체.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EquipmentFlangeNutCommand : IExternalCommand
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
                // 2) 트랜잭션 안에서 실행 (파라미터 해제 + 부품 이동/연결)
                EquipmentFlangeNutHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "장비&플랜지/NUT 연결"))
                {
                    tx.Start();
                    runResult = EquipmentFlangeNutHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                string summary =
                    $"장비(Mechanical Equipment): {runResult.EquipmentCount}대\n" +
                    $"파라미터 해제: {runResult.ParamUncheckedCount}개\n\n" +
                    $"FLANGE - 대상 인식: {runResult.FlangeTargetCount}개 / " +
                    $"연결 성공: {runResult.FlangeConnectedCount}개 / 실패: {runResult.FlangeFailedCount}개\n" +
                    $"NUT - 대상 인식: {runResult.NutTargetCount}개 / " +
                    $"연결 성공: {runResult.NutConnectedCount}개 / 실패: {runResult.NutFailedCount}개";

                TaskDialog.Show("장비&플랜지/NUT", summary);
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
