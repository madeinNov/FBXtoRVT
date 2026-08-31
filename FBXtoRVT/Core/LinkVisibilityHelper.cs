using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "LINK ON/OFF" 기능의 핵심 로직.
    ///
    /// 대상은 <b>좌표조정 모델(Coordination Model) 링크</b> 하나뿐이다.
    /// Navisworks 파일(.nwc / .nwd)을 Insert &gt; Coordination Model 로 붙인 그 링크이며,
    /// Revit 카테고리로는 "Coordination Model"(OST_Coordination_Model) 이다.
    /// RVT 링크는 건드리지 않는다.
    ///
    /// [왜 "뷰 필터" 로 처리하는가]
    /// 좌표조정 모델은 흔한 방법으로는 켜고 끌 수 없다. 실제로 확인한 결과는 이렇다.
    ///
    ///   - <c>View.SetCategoryHidden</c>  -&gt; "Category cannot be hidden" 예외
    ///   - <c>View.HideElements</c>       -&gt; "One of the elements cannot be hidden" 예외
    ///   - 뷰의 파라미터 목록에 좌표조정 모델 관련 항목이 아예 없음
    ///
    /// 이유는 좌표조정 모델이 V/G 대화상자에서 "Model Categories" 탭이 아니라
    /// <b>별도의 "Coordination Models" 탭</b>에 있기 때문이다.
    /// 카테고리 가시성 표에 칸 자체가 없으니, 그 표에 값을 쓰는 API 는 쓸 수 없다.
    /// (객체 숨기기도 카테고리가 가시성 제어를 지원해야 되므로 같은 이유로 막힌다)
    ///
    /// 반면 <b>뷰 필터</b>는 그 표와 상관없이 "화면을 그릴 때 걸러내는" 방식이라 동작한다.
    /// 다만 보통의 필터는 카테고리 기준이고 좌표조정 모델은 필터 가능 카테고리가 아닐 수 있으므로,
    /// <b>객체 Id 목록으로 대상을 지정하는 선택 필터(SelectionFilterElement)</b> 를 쓴다.
    ///
    /// [알아둘 점]
    /// 이 방법은 "Coordination Models" 탭의 체크를 실제로 해제하는 것이 아니다.
    /// 체크는 켜진 채로 두고 화면에만 안 보이게 하는 것이라, 눈에 보이는 결과만 같다.
    /// 애드인이 만든 필터는 V/G 의 Filters 탭에 이름으로 보이며, 지워도 다시 만들어진다.
    /// </summary>
    public static class LinkVisibilityHelper
    {
        // 대상 카테고리: 좌표조정 모델(Navisworks 링크)
        private const BuiltInCategory CoordinationModelCategory = BuiltInCategory.OST_Coordination_Model;

        // 좌표조정 모델을 켜고 끄기 위해 이 애드인이 만들어 쓰는 "선택 필터" 이름.
        // 뷰의 V/G > Filters 탭에 이 이름으로 보인다. 지워도 다음 실행 때 다시 만들어진다.
        private const string SelectionFilterName = "FBXtoRVT 좌표조정모델 ON/OFF";

        /// <summary>토글 결과.</summary>
        public class ToggleResult
        {
            public bool NowVisible; // 토글 후 켜짐이면 true
            public int LinkCount;   // 다룬 좌표조정 모델 수
        }

        /// <summary>
        /// 좌표조정 모델 링크의 가시성을 토글한다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        public static ToggleResult ToggleLinkVisibility(Document doc, View view)
        {
            // 문서 전체에서 좌표조정 모델을 모은다.
            //
            // 주의: 뷰를 지정한 FilteredElementCollector(doc, view.Id) 는 "그 뷰에 보이는" 객체만
            // 돌려주므로, 한 번 끄고 나면 다시 찾지 못해 켤 수가 없다. 그래서 문서 전체로 모은다.
            List<ElementId> linkIds = CollectCoordinationModelIds(doc);

            if (linkIds.Count == 0)
                throw new InvalidOperationException(
                    "이 문서에 좌표조정 모델(Coordination Model) 링크가 없어서 켜고 끌 것이 없습니다.");

            try
            {
                SelectionFilterElement filter = GetOrCreateFilter(doc);

                // 대상 객체를 항상 최신으로 맞춘다. (링크가 바뀌거나 늘어날 수 있으므로)
                filter.SetElementIds(linkIds);

                // 이 뷰에 아직 안 걸려 있으면 건다. (뷰마다 한 번씩만 일어난다)
                if (!view.GetFilters().Any(id => id == filter.Id))
                {
                    view.AddFilter(filter.Id);
                    LogUtils.Log($"뷰 '{view.Name}' 에 선택 필터 '{SelectionFilterName}' 를 걸었습니다.");
                }

                bool currentlyVisible = view.GetFilterVisibility(filter.Id);
                view.SetFilterVisibility(filter.Id, !currentlyVisible);

                LogUtils.Log($"LINK ON/OFF: 뷰='{view.Name}' 좌표조정모델 {linkIds.Count}개 -> " +
                    (currentlyVisible ? "꺼짐" : "켜짐"));

                return new ToggleResult
                {
                    NowVisible = !currentlyVisible,
                    LinkCount = linkIds.Count
                };
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"좌표조정 모델 가시성 토글 실패. 뷰='{view.Name}'");
                throw new InvalidOperationException(BuildFailMessage(doc, view, ex), ex);
            }
        }

        /// <summary>
        /// 이 애드인이 쓰는 선택 필터를 찾아온다. 없으면 새로 만든다.
        /// </summary>
        private static SelectionFilterElement GetOrCreateFilter(Document doc)
        {
            SelectionFilterElement filter = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(f => f.Name == SelectionFilterName);

            if (filter != null) return filter;

            filter = SelectionFilterElement.Create(doc, SelectionFilterName);
            LogUtils.Log($"선택 필터 '{SelectionFilterName}' 을 새로 만들었습니다. Id={filter.Id}");

            return filter;
        }

        /// <summary>
        /// 문서에 들어있는 좌표조정 모델(Coordination Model) 객체 Id 를 모은다.
        /// </summary>
        private static List<ElementId> CollectCoordinationModelIds(Document doc)
        {
            var ids = new List<ElementId>();

            foreach (Element e in new FilteredElementCollector(doc)
                .OfCategory(CoordinationModelCategory)
                .WhereElementIsNotElementType())
            {
                ids.Add(e.Id);
            }

            return ids;
        }

        /// <summary>
        /// 실패했을 때 사용자에게 보여줄 안내 문구.
        /// 뷰 템플릿이 필터를 잠그고 있으면 그게 원인인 경우가 많으므로 템플릿 이름을 같이 알려준다.
        /// </summary>
        private static string BuildFailMessage(Document doc, View view, Exception ex)
        {
            string message = $"현재 뷰('{view.Name}')에서 좌표조정 모델을 켜고 끄지 못했습니다.\n\n{ex.Message}";

            var template = doc.GetElement(view.ViewTemplateId) as View;
            if (template != null)
            {
                message += $"\n\n이 뷰에는 뷰 템플릿 '{template.Name}' 이(가) 걸려 있습니다. " +
                           "템플릿이 필터를 제어하고 있으면 뷰에서 바꿀 수 없으니, " +
                           "템플릿에서 필터 항목의 제어를 풀어 주세요.";
            }

            return message;
        }
    }
}
