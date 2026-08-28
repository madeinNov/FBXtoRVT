using System;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "LINK ON/OFF" 기능의 핵심 로직.
    /// 현재 뷰에서 링크된 RVT 모델(Coordination Model, "RVT Links" 카테고리)의
    /// 가시성을 켜져 있으면 끄고, 꺼져 있으면 켠다.
    /// </summary>
    public static class LinkVisibilityHelper
    {
        /// <summary>
        /// 링크 가시성을 토글한다. (외부에서 Transaction 을 열고 호출)
        /// </summary>
        /// <returns>토글 후 켜짐 상태면 true, 꺼짐 상태면 false</returns>
        public static bool ToggleLinkVisibility(Document doc, View view)
        {
            Category linkCategory = Category.GetCategory(doc, BuiltInCategory.OST_RvtLinks);
            if (linkCategory == null)
                throw new InvalidOperationException("'RVT Links' 카테고리를 찾지 못했습니다.");

            if (!linkCategory.get_AllowsVisibilityControl(view))
                throw new InvalidOperationException("현재 뷰에서는 링크 가시성을 제어할 수 없습니다.");

            bool currentlyHidden = view.GetCategoryHidden(linkCategory.Id);
            bool newHidden = !currentlyHidden;

            view.SetCategoryHidden(linkCategory.Id, newHidden);

            return !newHidden; // 켜짐 상태면 true
        }
    }
}
