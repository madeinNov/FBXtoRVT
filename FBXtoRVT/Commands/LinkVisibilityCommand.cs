using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "LINK ON/OFF" 버튼(단축키용)이 실행하는 명령.
    /// 현재 뷰에서 링크된 RVT 모델(Coordination Model)의 가시성을 켜짐/꺼짐 토글한다.
    /// 단축키로 반복 실행하는 용도이므로 대화상자 없이 조용히 토글만 한다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LinkVisibilityCommand : IExternalCommand
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
                using (Transaction tx = new Transaction(doc, "LINK ON/OFF"))
                {
                    tx.Start();
                    LinkVisibilityHelper.ToggleLinkVisibility(doc, activeView);
                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (InvalidOperationException ex)
            {
                TaskDialog.Show("LINK ON/OFF", ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
