using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "SCR장비&amp;플랜지/NUT" 기능.
    ///
    /// 대상은 <b>패밀리명에 'SCRUBBER' 가 포함된 장비</b>이고, 장비 바운딩 박스는 키우지 않는다.
    /// 그 밖의 규칙(부품을 어떻게 고르고 어떤 파라미터를 해제해 연결하는지)은
    /// <see cref="FlangeNutAttachHelper"/> 에 모아 두었다.
    /// (같은 규칙을 쓰는 "장비&amp;플랜지/NUT" 은 <see cref="EquipmentFlangeNutHelper"/>)
    /// </summary>
    public static class ScrubberFlangeHelper
    {
        // 기능 이름 (결과 대화상자 제목 / 로그에 함께 쓴다)
        public const string FeatureName = "SCR장비&플랜지/NUT";

        // SCRUBBER 는 바운딩 박스를 키우지 않고 그대로 쓴다.
        private const double BoxExpandFeet = 0.0;

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static FlangeNutAttachHelper.RunResult Run(Document doc, View view)
        {
            // 처리 도중 부품이 이동하므로, 장비는 Id 목록으로 먼저 확정해 둔다.
            var scrubberIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Scrubber))
            {
                scrubberIds.Add(fi.Id);
            }

            return FlangeNutAttachHelper.Run(doc, view, scrubberIds, BoxExpandFeet, FeatureName);
        }
    }
}
