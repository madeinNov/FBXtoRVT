using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "연결" 버튼이 실행하는 명령.
    /// 흐름: 첫 객체 클릭 > 두 번째 객체 클릭 > 두 객체의 가장 가까운 열린 커넥터끼리 연결.
    /// 두 번째 객체가 움직여서 붙는다. (배관이면 이동 대신 Stretch)
    ///
    /// 이 기능은 경고창 / 결과창을 띄우지 않는다.
    /// 조건이 맞지 않거나 오류가 나면 조용히 취소하고 로그 파일에만 남긴다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ObjectConnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

            // 1) 열린 문서 확인
            if (uiDoc == null || uiDoc.Document == null)
                return Result.Cancelled;

            Document doc = uiDoc.Document;

            try
            {
                // 2) 첫 번째 / 두 번째 객체 선택 (카테고리 제한 없음)
                Reference ref1 = uiDoc.Selection.PickObject(
                    ObjectType.Element, "연결의 기준이 될 첫 번째 객체를 클릭하세요. (움직이지 않음)");
                Element firstElem = doc.GetElement(ref1);

                Reference ref2 = uiDoc.Selection.PickObject(
                    ObjectType.Element, "붙일 두 번째 객체를 클릭하세요. (이 객체가 움직임)");
                Element secondElem = doc.GetElement(ref2);

                if (firstElem == null || secondElem == null)
                    return Result.Cancelled;

                // 3) 같은 객체를 두 번 골랐으면 커넥터 계산 없이 바로 취소
                if (firstElem.Id == secondElem.Id)
                    return Result.Cancelled;

                // 4) 트랜잭션 안에서 연결. 실패하면 롤백.
                using (Transaction tx = new Transaction(doc, "연결"))
                {
                    tx.Start();

                    bool connected = ObjectConnectHelper.Connect(doc, firstElem, secondElem);

                    if (connected)
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
                // (Result.Failed 로 돌려주면 Revit 이 오류창을 띄우므로 Cancelled 를 쓴다)
                LogUtils.LogError(ex, "연결 실행 실패");
                return Result.Cancelled;
            }
        }
    }
}
