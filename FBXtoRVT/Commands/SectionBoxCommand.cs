using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "선택 Section Box" 버튼이 실행하는 명령.
    /// 객체를 선택한 상태에서 실행하면, 선택 객체를 감싸는 Section Box 를
    /// tolerance 50mm 여유를 주어 현재 3D 뷰에 적용한다. (3D 뷰에서만 동작)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SectionBoxCommand : IExternalCommand
    {
        // 적용할 여유(tolerance) 값. 요구사항: 50mm
        private const int ToleranceMm = 50;

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

            // 2) 현재 뷰가 3D 뷰인지 확인
            View3D view3D = doc.ActiveView as View3D;
            if (view3D == null || view3D.IsTemplate)
            {
                TaskDialog.Show("선택 Section Box", "3D 뷰에서만 사용할 수 있습니다.");
                return Result.Cancelled;
            }

            // 3) 선택 객체 확인
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("선택 Section Box", "객체를 먼저 선택한 뒤 실행하세요.");
                return Result.Cancelled;
            }

            try
            {
                // 4) 트랜잭션 안에서 Section Box 적용
                SectionBoxHelper.RunResult runResult;

                using (Transaction tx = new Transaction(doc, "선택 Section Box 적용"))
                {
                    tx.Start();
                    runResult = SectionBoxHelper.Apply(doc, view3D, selectedIds, ToleranceMm);
                    tx.Commit();
                }

                if (!runResult.Applied)
                {
                    TaskDialog.Show("선택 Section Box", "선택 객체에서 바운딩 박스를 얻지 못했습니다.");
                    return Result.Cancelled;
                }

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
