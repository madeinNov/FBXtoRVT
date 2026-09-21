using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "SCR장비&플랜지/NUT" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    ///
    /// 처리 규칙은 "플랜지/NUT/VCR"(<see cref="EquipmentFlangeNutCommand"/>)과 완전히 같고,
    /// 대상 장비만 패밀리명에 'SCRUBBER' 가 들어간 것으로 좁힌다.
    /// (로직은 <see cref="EquipmentFlangeNutHelper"/> 하나를 함께 쓴다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ScrubberFlangeCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "SCR장비&플랜지/NUT";

        // 대상 장비 패밀리명 키워드
        private const string ScrubberFamilyKeyword = "SCRUBBER";

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
                // 2) 트랜잭션 안에서 실행 (부품 이동/연결). 장비 범위만 SCRUBBER 로 제한.
                EquipmentFlangeNutHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "SCR장비&플랜지/NUT 연결"))
                {
                    tx.Start();
                    runResult = EquipmentFlangeNutHelper.Run(doc, activeView, ScrubberFamilyKeyword);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                TaskDialog.Show(FeatureTitle, runResult.BuildSummary("SCRUBBER"));
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
