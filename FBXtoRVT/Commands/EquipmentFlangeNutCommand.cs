using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "장비&amp;플랜지 등" 버튼이 실행하는 명령. (예전 이름: 플랜지/NUT/VCR, 장비&amp;플랜지/NUT)
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// Mechanical Equipment 전체(SCR 장비 포함)를 대상으로 FLANGE / NUT / VCR 부품을 장비 커넥터에 붙인다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EquipmentFlangeNutCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "장비&플랜지 등";

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

                using (Transaction tx = new Transaction(doc, "장비&플랜지 등 연결"))
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
