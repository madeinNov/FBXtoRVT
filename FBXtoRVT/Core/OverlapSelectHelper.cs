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
    /// </summary>
    public static class OverlapSelectHelper
    {
        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int MainCount;            // 수집한 Main 객체 수
            public int SubCount;             // 수집한 Sub 객체 수
            public List<ElementId> Selected; // 선택 대상 Sub 객체 Id 목록

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
            var mainBoxes = new List<ElementUtils.WorldBox>();
            foreach (Element e in CollectFamilyInstancesInView(doc, view, mainKeyword))
            {
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

                if (NameContains(e, keyword))
                    yield return e;
            }
        }

        /// <summary>
        /// 패밀리명 또는 타입(Symbol)명에 키워드가 포함되는지(대소문자 무시).
        /// </summary>
        private static bool NameContains(Element e, string keyword)
        {
            var fi = e as FamilyInstance;
            if (fi == null || fi.Symbol == null) return false;

            string typeName = fi.Symbol.Name ?? "";
            string familyName = (fi.Symbol.Family != null) ? (fi.Symbol.Family.Name ?? "") : "";

            return typeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
