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
    /// "직각 배관 생성기" 버튼이 실행하는 명령.
    /// 흐름: 첫 배관 클릭(배관만 가능) > 두 번째 객체 클릭(카테고리 제한 없음) > 직각 배관 생성.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RightAnglePipeCommand : IExternalCommand
    {
        // 대화상자 제목
        private const string FeatureTitle = "직각 배관 생성기";

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
                // 2) 첫 번째 객체는 반드시 배관 (PipeSelectionFilter 로 배관만 클릭 가능)
                Reference baseRef = uiDoc.Selection.PickObject(
                    ObjectType.Element, new PipeSelectionFilter(), "기준이 될 배관을 클릭하세요.");
                Pipe basePipe = doc.GetElement(baseRef) as Pipe;

                if (basePipe == null)
                {
                    message = "첫 번째 배관 선택에 실패했습니다.";
                    return Result.Failed;
                }

                // 3) 두 번째 객체는 카테고리 제한 없음 (필터 없이 선택)
                Reference targetRef = uiDoc.Selection.PickObject(
                    ObjectType.Element, "연결할 대상 객체를 클릭하세요. (배관 / 그 밖의 객체 모두 가능)");
                Element target = doc.GetElement(targetRef);

                if (target == null)
                {
                    message = "두 번째 객체 선택에 실패했습니다.";
                    return Result.Failed;
                }

                if (basePipe.Id == target.Id)
                {
                    TaskDialog.Show(FeatureTitle, "서로 다른 두 객체를 선택하세요.");
                    return Result.Cancelled;
                }

                // 4) 트랜잭션 안에서 직각 배관 생성
                using (Transaction tx = new Transaction(doc, "직각 배관 생성"))
                {
                    tx.Start();
                    RightAnglePipeHelper.CreateRightAnglePipe(doc, basePipe, target);
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
                // 커넥터가 모두 닫힘 등 조건 불충족: 안내 후 종료 (트랜잭션은 롤백됨)
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
