using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "겹침 객체 선택" 기능의 핵심 로직.
    ///
    /// Main 문자열을 포함하는 객체의 바운딩 박스 안에, Sub 문자열을 포함하는 객체의
    /// 중심점이 들어가면(= 두 객체가 겹쳐 있으면) 그 Sub 객체를 선택 대상으로 골라낸다.
    /// 겹쳐서 불필요하게 남아 있는 Sub 객체를 한 번에 확인/삭제하려는 용도이다.
    ///
    /// Main 객체 자신은 선택하지 않는다.
    /// (Main 이름이 Sub 조건까지 만족하는 경우가 있는데, 그때 Main 까지 같이 선택되면
    ///  지우려던 Sub 와 남겨야 할 Main 이 섞여 위험하다)
    /// </summary>
    public static class OverlapSelectHelper
    {
        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int MainCount;                 // 수집한 Main 객체 수
            public int SubCount;                  // 수집한 Sub 객체 수 (Main 자신은 뺀 수)
            public int MainAlsoMatchedSubCount;   // Sub 조건도 만족했지만 Main 이라 제외한 객체 수
            public List<ElementId> Selected;      // 선택 대상 Sub 객체 Id 목록

            public RunResult()
            {
                Selected = new List<ElementId>();
            }
        }

        /// <summary>
        /// 메인 실행. (선택은 UI 작업이므로 트랜잭션이 필요 없음)
        /// Main 문자열을 포함하는 객체의 바운딩 박스 안에, Sub 문자열을 포함하는 객체의
        /// 중심점이 들어가면 그 Sub 객체를 선택 대상으로 반환한다.
        /// </summary>
        public static RunResult Run(Document doc, View view, string mainKeyword, string subKeyword)
        {
            var result = new RunResult();

            // 1) Main 문자열을 포함하는 객체들의 바운딩 박스 수집
            //    (Main 자신을 선택에서 빼기 위해 Id 도 같이 기억해 둔다)
            var mainBoxes = new List<ElementUtils.WorldBox>();
            var mainIds = new HashSet<ElementId>();

            foreach (Element e in CollectFamilyInstancesInView(doc, view, mainKeyword))
            {
                mainIds.Add(e.Id);

                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(e);
                if (box != null)
                {
                    mainBoxes.Add(box);
                    result.MainCount++;
                }
            }

            // 2) Sub 문자열을 포함하는 객체들의 중심점이 Main 박스 안에 들어가면 선택 대상
            foreach (Element e in CollectFamilyInstancesInView(doc, view, subKeyword))
            {
                // Main 객체 자신은 (Sub 조건까지 만족하더라도) 선택하지 않는다
                if (mainIds.Contains(e.Id))
                {
                    result.MainAlsoMatchedSubCount++;
                    continue;
                }

                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(e);
                if (box == null) continue;

                result.SubCount++;

                XYZ center = box.Center;

                // 하나라도 Main 박스 안에 들어가면(= 겹치면) 선택
                foreach (ElementUtils.WorldBox mainBox in mainBoxes)
                {
                    if (mainBox.Contains(center))
                    {
                        result.Selected.Add(e.Id);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 현재 뷰에서 키워드(패밀리명 또는 타입명에 포함, 대소문자 무시)에 맞는 FamilyInstance 수집.
        /// </summary>
        private static IEnumerable<Element> CollectFamilyInstancesInView(Document doc, View view, string keyword)
        {
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance));

            foreach (Element e in collector)
            {
                // 규칙 1: 복합 패밀리 안의 Sub-Component 는 기능 대상이 아님
                if (ElementUtils.IsSubComponent(e)) continue;

                // 패밀리명 또는 타입명 어느 쪽에 들어 있어도 대상으로 본다
                if (ElementUtils.NameContains(e, keyword))
                    yield return e;
            }
        }
    }
}
