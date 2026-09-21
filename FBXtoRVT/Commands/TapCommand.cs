using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "TAP" 버튼이 실행하는 명령.
    ///
    /// 흐름
    ///   1) 객체를 미리 선택해 두었으면 그 객체들을, 아니면 다중 선택(마침 버튼)으로 객체를 고른다.
    ///   2) 선택한 객체 하나하나에 대해 TAP 을 적용한다. (조건이 맞지 않는 객체는 건너뛴다)
    ///
    /// 이 기능은 결과창을 띄우지 않는다. 처리 내용은 로그 파일에만 남긴다.
    /// 전체가 하나의 Transaction 이므로 Undo 한 번에 모두 되돌아간다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

            // 1) 열린 문서 확인
            if (uiDoc == null || uiDoc.Document == null)
                return Result.Cancelled;

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;
            if (view == null || view.IsTemplate)
                return Result.Cancelled;

            try
            {
                // 2) 대상 객체: 미리 선택한 것이 있으면 그대로, 없으면 다중 선택
                ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();

                if (selectedIds == null || selectedIds.Count == 0)
                {
                    IList<Reference> refs = uiDoc.Selection.PickObjects(
                        ObjectType.Element, "TAP 을 적용할 2커넥터 피팅을 선택한 뒤 '마침' 을 누르세요.");

                    selectedIds = refs.Select(r => r.ElementId).ToList();
                }

                if (selectedIds.Count == 0)
                    return Result.Cancelled;

                // 3) 트랜잭션 안에서 처리. 배관 생성조차 하나도 못 했으면 롤백해서 Undo 목록에 남기지 않는다.
                using (Transaction tx = new Transaction(doc, "TAP"))
                {
                    tx.Start();

                    TapHelper.TapResult runResult = TapHelper.Run(doc, view, selectedIds);

                    if (runResult.ProcessedCount > 0)
                        tx.Commit();
                    else
                        tx.RollBack();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 사용자가 ESC 로 취소
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // 창을 띄우지 않기로 했으므로 로그만 남기고 조용히 끝낸다.
                LogUtils.LogError(ex, "TAP 실행 실패");
                return Result.Cancelled;
            }
        }
    }
}
