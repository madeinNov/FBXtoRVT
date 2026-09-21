using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "플랜지/NUT/VCR" 버튼이 실행하는 명령. (예전 이름: 장비&amp;플랜지/NUT)
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// ScrubberFlangeCommand 와 동일하되, 대상이 'SCRUBBER' 가 아니라 Mechanical Equipment 전체이고,
    /// FLANGE / NUT 에 더해 VCR 부품(Primary 가 장비쪽)도 붙인다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EquipmentFlangeNutCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "플랜지/NUT/VCR";

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
                // 2) 트랜잭션 안에서 실행 (부품 이동/연결)
                EquipmentFlangeNutHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "플랜지/NUT/VCR 연결"))
                {
                    tx.Start();
                    runResult = EquipmentFlangeNutHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                TaskDialog.Show(FeatureTitle, runResult.BuildSummary("장비(Mechanical Equipment)"));
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
