using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "현재 뷰 전체를 훑어서 한 번에 처리하는" 명령들의 공통 뼈대.
    ///
    /// 이런 명령은 매번 아래 순서가 똑같다.
    ///   1) 열린 문서 / 활성 뷰가 있는지 확인
    ///   2) Transaction 을 열고 Core 의 로직을 실행
    ///   3) 결과 요약을 대화상자로 표시
    ///   4) 예외 처리
    ///
    /// 그래서 1 / 2 / 4 는 이 클래스가 대신 처리하고,
    /// 각 명령은 <see cref="FeatureTitle"/> 과 <see cref="RunInTransaction"/> 만 채우면 된다.
    ///
    /// 사용자가 클릭으로 객체를 먼저 골라야 하는 명령(대각 배관 생성기 등)은
    /// 흐름이 달라서 이 뼈대를 쓰지 않는다.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public abstract class ViewCommandBase : IExternalCommand
    {
        /// <summary>결과 / 안내 대화상자의 제목.</summary>
        protected abstract string FeatureTitle { get; }

        /// <summary>
        /// 실행 취소(Undo) 목록에 보일 이름. 따로 정하지 않으면 대화상자 제목을 그대로 쓴다.
        /// </summary>
        protected virtual string TransactionName
        {
            get { return FeatureTitle; }
        }

        /// <summary>
        /// 트랜잭션 안에서 실제로 할 일. (Transaction 은 이 클래스가 열고 닫는다)
        /// </summary>
        /// <returns>
        /// 대화상자로 보여줄 결과 요약. 알려줄 것이 없으면 null 을 돌려주면 창을 띄우지 않는다.
        /// </returns>
        protected abstract string RunInTransaction(Document doc, View view);

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
                // 2) 트랜잭션 안에서 기능 실행
                string summary;

                using (Transaction tx = new Transaction(doc, TransactionName))
                {
                    tx.Start();
                    summary = RunInTransaction(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시 (트랜잭션을 닫은 뒤에 띄운다)
                if (!string.IsNullOrEmpty(summary))
                {
                    TaskDialog.Show(FeatureTitle, summary);
                }

                return Result.Succeeded;
            }
            catch (InvalidOperationException ex)
            {
                // 조건이 맞지 않아 못 하는 경우: 안내만 하고 종료 (트랜잭션은 롤백된다)
                TaskDialog.Show(FeatureTitle, ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"{FeatureTitle} 실행 실패.");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
