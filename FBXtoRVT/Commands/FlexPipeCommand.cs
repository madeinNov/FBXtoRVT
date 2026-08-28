using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "Flex Pipe 생성기" 버튼이 실행하는 명령.
    /// 흐름: 첫 객체 클릭 > 두 번째 객체 클릭 > 첫 객체의 열린 커넥터에서
    /// 둘째 객체의 열린 커넥터까지 FLEX PIPE 를 생성한다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlexPipeCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "Flex Pipe 생성기";

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
                // 2) 첫 번째 / 두 번째 객체 선택 (카테고리 제한 없음)
                Reference ref1 = uiDoc.Selection.PickObject(
                    ObjectType.Element, "첫 번째 객체를 클릭하세요.");
                Element obj1 = doc.GetElement(ref1);

                Reference ref2 = uiDoc.Selection.PickObject(
                    ObjectType.Element, "두 번째 객체를 클릭하세요.");
                Element obj2 = doc.GetElement(ref2);

                if (obj1 == null || obj2 == null)
                {
                    message = "객체 선택에 실패했습니다.";
                    return Result.Failed;
                }

                if (obj1.Id == obj2.Id)
                {
                    TaskDialog.Show(FeatureTitle, "서로 다른 두 객체를 선택하세요.");
                    return Result.Cancelled;
                }

                // 3) 트랜잭션 안에서 FLEX PIPE 생성
                using (Transaction tx = new Transaction(doc, "Flex Pipe 생성"))
                {
                    tx.Start();
                    FlexPipeHelper.CreateFlexPipe(doc, obj1, obj2);
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
                // 열린 커넥터가 없는 등 조건 불충족: 안내 후 종료 (트랜잭션은 롤백됨)
                TaskDialog.Show(FeatureTitle, ex.Message);
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
