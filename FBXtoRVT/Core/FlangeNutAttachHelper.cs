using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "장비 안의 FLANGE / NUT 을 장비의 열린 커넥터에 붙이는" 공통 로직.
    ///
    /// 이 규칙을 쓰는 기능이 두 개다.
    ///   - <see cref="ScrubberFlangeHelper"/>       : 대상이 패밀리명에 'SCRUBBER' 가 든 장비 (박스 확장 없음)
    ///   - <see cref="EquipmentFlangeNutHelper"/>   : 대상이 Mechanical Equipment 카테고리 전체 (박스 +20mm)
    ///
    /// 예전에는 두 파일이 같은 코드를 각각 갖고 있어서, 한쪽만 고치면 규칙이 갈라질 위험이 있었다.
    /// 그래서 "어떤 장비를 대상으로 할지" 와 "박스를 얼마나 키울지" 만 인자로 받고,
    /// 나머지 규칙은 전부 이 파일 하나에 둔다.
    ///
    /// 처리 흐름 (장비 1대 기준)
    ///  1) 장비의 바운딩 박스(필요하면 확장)와 열린 커넥터를 모은다.
    ///  2) 그 박스 안에 중심점이 들어가는 부품(FLANGE / NUT)을 고른다.
    ///  3) 부품 바운딩 박스 안에 장비의 열린 커넥터가 "정확히 1개" 들어있으면,
    ///     그 커넥터를 그 부품의 대상 커넥터로 인식한다.
    ///  4) 부품의 열린 커넥터 개수에 따라 파라미터를 해제하고, 부품을 이동/회전시켜 연결한다.
    ///
    ///  FLANGE — 어느 커넥터를 쓸지만 정하고, 해제할 파라미터는 <see cref="PartSideTable"/> 이 정한다.
    ///   - 열린 커넥터 2개                : Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개(Primary)       : 위와 동일
    ///   - 열린 커넥터 1개(Primary 아님)  : 그 열린 커넥터를 대상 커넥터에 연결
    ///
    ///  해제 규칙은 "지금 붙이는 커넥터 쪽 플랜지를 해제한다" 하나뿐이다.
    ///  그 커넥터가 상인지 하인지는 패밀리마다 다르므로 <see cref="PartSideTable"/> 의 표를 본다.
    ///
    ///  NUT (파라미터 해제 없음)
    ///   - 열린 커넥터 2개 : Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개 : 그 열린 커넥터를 대상 커넥터에 연결
    /// </summary>
    public static class FlangeNutAttachHelper
    {
        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int EquipmentCount;        // 찾은 장비 수
            public int FlangeTargetCount;     // 대상 커넥터를 인식한 FLANGE 수
            public int FlangeConnectedCount;  // FLANGE 연결 성공 수
            public int FlangeFailedCount;     // FLANGE 연결 실패 수
            public int NutTargetCount;        // 대상 커넥터를 인식한 NUT 수
            public int NutConnectedCount;     // NUT 연결 성공 수
            public int NutFailedCount;        // NUT 연결 실패 수
            public int ParamUncheckedCount;   // 실제로 해제한 파라미터 수
        }

        /// <summary>
        /// 부품 하나를 "Id + 중심점" 으로 기억해 두는 참조.
        ///
        /// 부품 후보는 <b>실행 시작 때 한 번만</b> 수집한다.
        /// (장비마다 FilteredElementCollector 를 다시 돌리면 큰 모델에서 매우 느려진다)
        /// 부품이 실제로 이동하면 그때 중심점을 다시 계산해 최신 상태로 유지한다.
        /// </summary>
        private class PartRef
        {
            public ElementId Id;
            public XYZ Center;
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        /// <param name="equipmentIds">대상 장비 Id 목록. 어떤 장비를 고를지는 호출하는 쪽이 정한다.</param>
        /// <param name="boxExpandFeet">장비 바운딩 박스를 모든 방향으로 키울 양(feet). 키우지 않으려면 0.</param>
        /// <param name="featureName">로그에 남길 기능 이름</param>
        public static RunResult Run(Document doc, View view, List<ElementId> equipmentIds,
            double boxExpandFeet, string featureName)
        {
            var result = new RunResult();
            result.EquipmentCount = equipmentIds.Count;

            // 부품 후보는 여기서 한 번만 수집한다. (규칙: 반복문 안에서 다시 수집하지 않는다)
            List<PartRef> flangeParts = CollectParts(doc, view, FamilyKeywords.Flange);
            List<PartRef> nutParts = CollectParts(doc, view, FamilyKeywords.Nut);

            LogUtils.Log($"===== {featureName} 실행 시작. 장비 {result.EquipmentCount}대, " +
                $"FLANGE 후보 {flangeParts.Count}개, NUT 후보 {nutParts.Count}개 =====");

            // 이미 부품이 붙은 장비 커넥터는 다시 쓰지 않도록 기록
            var usedEquipConnKeys = new HashSet<string>();

            foreach (ElementId equipId in equipmentIds)
            {
                var equip = doc.GetElement(equipId) as FamilyInstance;
                if (equip == null) continue;

                ElementUtils.WorldBox equipBox = ElementUtils.GetWorldBox(equip);
                if (equipBox == null) continue;

                equipBox = equipBox.ExpandAll(boxExpandFeet);

                // 장비의 열린 커넥터 정보 수집
                var equipConns = new List<ConnRef>();
                foreach (Connector c in ElementUtils.GetOpenEndConnectors(equip))
                {
                    equipConns.Add(ConnRef.From(equipId, c));
                }

                if (equipConns.Count == 0) continue;

                // FLANGE 처리 → NUT 처리
                ProcessParts(doc, equipId, equipBox, equipConns, usedEquipConnKeys, flangeParts, true, result);
                ProcessParts(doc, equipId, equipBox, equipConns, usedEquipConnKeys, nutParts, false, result);
            }

            LogUtils.Log($"===== {featureName} 실행 종료. " +
                $"FLANGE 대상={result.FlangeTargetCount} 성공={result.FlangeConnectedCount} 실패={result.FlangeFailedCount} / " +
                $"NUT 대상={result.NutTargetCount} 성공={result.NutConnectedCount} 실패={result.NutFailedCount} / " +
                $"파라미터해제={result.ParamUncheckedCount} =====");

            return result;
        }

        /// <summary>
        /// 현재 뷰에서 부품 후보(FLANGE 또는 NUT)를 한 번만 수집한다.
        /// 바운딩 박스를 못 구하는 객체는 애초에 대상이 될 수 없으므로 여기서 뺀다.
        /// </summary>
        private static List<PartRef> CollectParts(Document doc, View view, string familyKeyword)
        {
            var list = new List<PartRef>();

            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, familyKeyword))
            {
                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(fi);
                if (box == null) continue;

                list.Add(new PartRef { Id = fi.Id, Center = box.Center });
            }

            return list;
        }

        /// <summary>
        /// 장비 박스 안에 있는 부품을 찾아 대상 커넥터에 연결한다.
        /// </summary>
        /// <param name="isFlange">true 면 FLANGE 규칙(파라미터 해제 포함), false 면 NUT 규칙</param>
        private static void ProcessParts(Document doc, ElementId equipId,
            ElementUtils.WorldBox equipBox, List<ConnRef> equipConns, HashSet<string> usedEquipConnKeys,
            List<PartRef> parts, bool isFlange, RunResult result)
        {
            foreach (PartRef part in parts)
            {
                if (part.Id == equipId) continue;                 // 장비 자신은 제외
                if (!equipBox.Contains(part.Center)) continue;    // 부품 중심점이 장비 박스 안에 있어야 대상

                ProcessOnePart(doc, part, equipConns, usedEquipConnKeys, isFlange, result);
            }
        }

        /// <summary>
        /// 부품 1개 처리. 대상 커넥터를 인식하지 못하면 아무것도 하지 않는다.
        /// </summary>
        private static void ProcessOnePart(Document doc, PartRef partRef, List<ConnRef> equipConns,
            HashSet<string> usedEquipConnKeys, bool isFlange, RunResult result)
        {
            ElementId partId = partRef.Id;

            var part = doc.GetElement(partId) as FamilyInstance;
            if (part == null) return;

            ElementUtils.WorldBox partBox = ElementUtils.GetWorldBox(part);
            if (partBox == null) return;

            // 1) 부품 박스 안에 들어있는 (아직 쓰지 않은) 장비 열린 커넥터 찾기
            ConnRef target = null;
            int insideCount = 0;

            foreach (ConnRef c in equipConns)
            {
                if (usedEquipConnKeys.Contains(c.Key)) continue;
                if (!partBox.Contains(c.Origin)) continue;

                insideCount++;
                target = c;
            }

            // 정확히 1개일 때만 대상 커넥터로 인식
            if (insideCount != 1) return;

            // 2) 부품의 열린 커넥터 개수에 따라 어느 커넥터를 쓸지 결정
            List<Connector> openConns = ElementUtils.GetOpenEndConnectors(part);

            bool usePrimary;              // Primary 커넥터를 쓸지 여부
            int chosenConnectorId = -1;   // Primary 를 쓰지 않을 때 사용할 커넥터 Id

            if (openConns.Count == 2)
            {
                // 열린 커넥터 2개 → Primary 커넥터 사용
                usePrimary = true;
            }
            else if (openConns.Count == 1)
            {
                Connector only = openConns[0];

                if (!isFlange || !ElementUtils.IsPrimaryConnector(only))
                {
                    // NUT 이거나, Primary 가 아닌 커넥터 하나뿐이면 그 커넥터를 그대로 사용
                    usePrimary = false;
                    chosenConnectorId = only.Id;
                }
                else
                {
                    // FLANGE + Primary → 2개일 때와 동일하게 처리
                    usePrimary = true;
                }
            }
            else
            {
                // 열린 커넥터가 0개이거나 3개 이상이면 대상이 아님
                return;
            }

            // 해제할 파라미터는 패밀리별 표가 정한다. (NUT 은 해제하지 않는다)
            List<string> paramsToUncheck = isFlange
                ? PartSideTable.GetParamsToUncheck(part, usePrimary)
                : new List<string>();

            if (isFlange) result.FlangeTargetCount++;
            else result.NutTargetCount++;

            if (LogUtils.DetailEnabled)
                LogUtils.LogDetail($"부품(Id={partId}, Family={ElementUtils.GetFamilyName(part)}) " +
                    $"열린커넥터={openConns.Count} Primary사용={usePrimary} " +
                    $"Primary쪽={PartSideTable.GetPrimarySide(part)} " +
                    $"해제파라미터={(paramsToUncheck.Count == 0 ? "(없음)" : string.Join(", ", paramsToUncheck))} " +
                    $"대상 장비커넥터={target.Key}");

            // 3) 파라미터 해제 (형상이 바뀌므로 이후 커넥터는 다시 조회한다)
            bool anyUnchecked = false;
            foreach (string paramName in paramsToUncheck)
            {
                if (!ElementUtils.UncheckYesNoParam(part, paramName)) continue;

                result.ParamUncheckedCount++;
                anyUnchecked = true;
            }

            if (anyUnchecked) doc.Regenerate();

            // 4) 실제로 연결할 커넥터를 다시 조회
            Connector subConn = usePrimary
                ? ElementUtils.GetPrimaryConnector(doc.GetElement(partId))
                : ElementUtils.ResolveConnector(doc, partId, chosenConnectorId);

            Connector targetConn = target.Resolve(doc);

            if (subConn == null || subConn.IsConnected || targetConn == null || targetConn.IsConnected)
            {
                AddFailed(isFlange, result);
                return;
            }

            // 5) 장비 커넥터를 기준(Main)으로 두고, 부품(Sub)을 이동/회전시켜 연결
            try
            {
                ConnectorHelper.AlignAndConnect(doc, targetConn, subConn, partId);
                doc.Regenerate();

                usedEquipConnKeys.Add(target.Key);

                // 부품이 움직였으므로 기억해 둔 중심점을 최신값으로 갱신한다.
                ElementUtils.WorldBox movedBox = ElementUtils.GetWorldBox(doc.GetElement(partId));
                if (movedBox != null) partRef.Center = movedBox.Center;

                if (isFlange) result.FlangeConnectedCount++;
                else result.NutConnectedCount++;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"부품(Id={partId}) 을(를) 장비 커넥터({target.Key})에 연결하지 못함.");
                AddFailed(isFlange, result);
            }
        }

        /// <summary>실패 수를 부품 종류에 맞게 증가.</summary>
        private static void AddFailed(bool isFlange, RunResult result)
        {
            if (isFlange) result.FlangeFailedCount++;
            else result.NutFailedCount++;
        }
    }
}
