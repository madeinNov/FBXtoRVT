using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "대각 배관 생성기" 버튼이 실행하는 명령.
    /// 흐름: 첫 배관 클릭 > 두 번째 배관 클릭 > 45도 대각 배관 생성.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DiagonalPipeCommand : IExternalCommand
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

            try
            {
                var filter = new PipeSelectionFilter();

                // 2) 첫 번째 배관 클릭
                Reference ref1 = uiDoc.Selection.PickObject(
                    ObjectType.Element, filter, "첫 번째 배관을 클릭하세요.");
                Pipe pipe1 = doc.GetElement(ref1) as Pipe;

                // 3) 두 번째 배관 클릭
                Reference ref2 = uiDoc.Selection.PickObject(
                    ObjectType.Element, filter, "두 번째 배관을 클릭하세요.");
                Pipe pipe2 = doc.GetElement(ref2) as Pipe;

                if (pipe1 == null || pipe2 == null)
                {
                    message = "배관 선택에 실패했습니다.";
                    return Result.Failed;
                }

                if (pipe1.Id == pipe2.Id)
                {
                    TaskDialog.Show("대각 배관 생성기", "서로 다른 두 배관을 선택하세요.");
                    return Result.Cancelled;
                }

                // 4) 트랜잭션 안에서 대각 배관 생성
                using (Transaction tx = new Transaction(doc, "대각 배관 생성"))
                {
                    tx.Start();
                    Pipe newPipe = DiagonalPipeHelper.CreateDiagonalPipe(doc, pipe1, pipe2);
                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 사용자가 ESC 로 취소
                return Result.Cancelled;
            }
            catch (InvalidOperationException ex)
            {
                // 평행하지 않음 등 조건 불충족: 안내 후 종료
                TaskDialog.Show("대각 배관 생성기", ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 배관(Pipe)만 선택 가능하도록 하는 선택 필터.
    /// </summary>
    public class PipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Pipe;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
