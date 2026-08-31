using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "SCR장비&플랜지/NUT" 기능의 핵심 로직.
    ///
    /// 처리 흐름 (SCRUBBER 1대 기준)
    ///  1) 패밀리명에 "SCRUBBER" 가 포함된 객체의 바운딩 박스와 열린 커넥터를 모은다.
    ///  2) 그 박스 안에 중심점이 들어가는 부품(FLANGE / NUT)을 모으고, 부품별 바운딩 박스를 구한다.
    ///  3) 부품 바운딩 박스 안에 장비의 열린 커넥터가 "정확히 1개" 들어있으면,
    ///     그 커넥터를 그 부품의 대상 커넥터로 인식한다.
    ///  4) 부품의 열린 커넥터 개수에 따라 파라미터를 해제하고, 부품을 이동/회전시켜 연결한다.
    ///
    ///  FLANGE
    ///   - 열린 커넥터 2개      : "FLANGE 하" 해제 후 Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개(Primary)      : 위와 동일
    ///   - 열린 커넥터 1개(Primary 아님)  : "FLANGE 상" 해제 후 그 열린 커넥터를 대상 커넥터에 연결
    ///
    ///  FLANGE 중 이름(패밀리명 또는 타입명)에 "BELLOWS" 가 들어간 것은 상/하가 반대다.
    ///   - Primary      : "FLANGE 상" 해제
    ///   - Primary 아님  : "FLANGE 하" 해제
    ///
    ///  NUT (파라미터 해제 없음)
    ///   - 열린 커넥터 2개 : Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개 : 그 열린 커넥터를 대상 커넥터에 연결
    /// </summary>
    public static class ScrubberFlangeHelper
    {
        // 대상 패밀리명 키워드
        private const string ScrubberFamilyKeyword = "SCRUBBER";
        private const string FlangeFamilyKeyword = "FLANGE";
        private const string NutFamilyKeyword = "NUT";

        // 상/하 해제 규칙이 반대가 되는 부품 이름 키워드
        private const string BellowsKeyword = "BELLOWS";

        // 해제 대상 YES/NO 인스턴스 파라미터 이름
        private const string ParamFlangeLower = "FLANGE 하";
        private const string ParamFlangeUpper = "FLANGE 상";

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int ScrubberCount;         // 찾은 SCRUBBER 수
            public int FlangeTargetCount;     // 대상 커넥터를 인식한 FLANGE 수
            public int FlangeConnectedCount;  // FLANGE 연결 성공 수
            public int FlangeFailedCount;     // FLANGE 연결 실패 수
            public int NutTargetCount;        // 대상 커넥터를 인식한 NUT 수
            public int NutConnectedCount;     // NUT 연결 성공 수
            public int NutFailedCount;        // NUT 연결 실패 수
            public int ParamUncheckedCount;   // 실제로 해제한 파라미터 수
        }

        /// <summary>
        /// 장비 커넥터를 "객체 Id + 커넥터 Id" 로 기억해 두는 참조.
        /// 부품을 붙이는 도중 문서가 바뀌므로, 쓰기 직전에 다시 조회한다.
        /// </summary>
        private class ConnRef
        {
            public ElementId OwnerId;
            public int ConnectorId;
            public XYZ Origin;

            /// <summary>중복 사용 방지를 위한 키</summary>
            public string Key
            {
                get { return OwnerId.Value + ":" + ConnectorId; }
            }
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            // 처리 도중 부품이 이동하므로, 장비는 Id 목록으로 먼저 확정해 둔다.
            var scrubberIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, ScrubberFamilyKeyword))
            {
                scrubberIds.Add(fi.Id);
            }
            result.ScrubberCount = scrubberIds.Count;

            // 이미 부품이 붙은 장비 커넥터는 다시 쓰지 않도록 기록
            var usedEquipConnKeys = new HashSet<string>();

            foreach (ElementId scrubberId in scrubberIds)
            {
                var scrubber = doc.GetElement(scrubberId) as FamilyInstance;
                if (scrubber == null) continue;

                ElementUtils.WorldBox scrubberBox = ElementUtils.GetWorldBox(scrubber);
                if (scrubberBox == null) continue;

                // 장비의 열린 커넥터 정보 수집
                var equipConns = new List<ConnRef>();
                foreach (Connector c in ElementUtils.GetOpenEndConnectors(scrubber))
                {
                    equipConns.Add(new ConnRef
                    {
                        OwnerId = scrubberId,
                        ConnectorId = c.Id,
                        Origin = c.Origin
                    });
                }

                if (equipConns.Count == 0) continue;

                // FLANGE 처리 → NUT 처리
                ProcessParts(doc, view, scrubberId, scrubberBox, equipConns, usedEquipConnKeys,
                    FlangeFamilyKeyword, true, result);

                ProcessParts(doc, view, scrubberId, scrubberBox, equipConns, usedEquipConnKeys,
                    NutFamilyKeyword, false, result);
            }

            return result;
        }

        /// <summary>
        /// 장비 박스 안의 부품(FLANGE 또는 NUT)을 찾아 대상 커넥터에 연결한다.
        /// </summary>
        /// <param name="isFlange">true 면 FLANGE 규칙(파라미터 해제 포함), false 면 NUT 규칙</param>
        private static void ProcessParts(Document doc, View view, ElementId scrubberId,
            ElementUtils.WorldBox scrubberBox, List<ConnRef> equipConns, HashSet<string> usedEquipConnKeys,
            string familyKeyword, bool isFlange, RunResult result)
        {
            // 처리 도중 부품이 이동하므로 Id 목록으로 먼저 확정
            var partIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, familyKeyword))
            {
                if (fi.Id == scrubberId) continue; // 장비 자신은 제외

                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(fi);
                if (box == null) continue;

                // 부품 중심점이 장비 박스 안에 있어야 대상
                if (!scrubberBox.Contains(box.Center)) continue;

                partIds.Add(fi.Id);
            }

            foreach (ElementId partId in partIds)
            {
                ProcessOnePart(doc, partId, equipConns, usedEquipConnKeys, isFlange, result);
            }
        }

        /// <summary>
        /// 부품 1개 처리. 대상 커넥터를 인식하지 못하면 아무것도 하지 않는다.
        /// </summary>
        private static void ProcessOnePart(Document doc, ElementId partId, List<ConnRef> equipConns,
            HashSet<string> usedEquipConnKeys, bool isFlange, RunResult result)
        {
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

            // 2) 부품의 열린 커넥터 개수에 따라 처리 방법 결정
            List<Connector> openConns = ElementUtils.GetOpenEndConnectors(part);

            // 이름에 BELLOWS 가 들어간 부품은 상/하 해제 규칙이 반대다
            bool isBellows = ElementUtils.NameContains(part, BellowsKeyword);

            string paramToUncheck = null; // 해제할 파라미터 (NUT 은 없음)
            bool usePrimary;              // Primary 커넥터를 쓸지 여부
            int chosenConnectorId = -1;   // Primary 를 쓰지 않을 때 사용할 커넥터 Id

            if (openConns.Count == 2)
            {
                // 열린 커넥터 2개 → Primary 커넥터 사용
                usePrimary = true;
                if (isFlange) paramToUncheck = GetFlangeParamToUncheck(true, isBellows);
            }
            else if (openConns.Count == 1)
            {
                Connector only = openConns[0];

                if (!isFlange)
                {
                    // NUT: 열린 커넥터를 그대로 사용
                    usePrimary = false;
                    chosenConnectorId = only.Id;
                }
                else if (ElementUtils.IsPrimaryConnector(only))
                {
                    // FLANGE + Primary → 2개일 때와 동일하게 처리
                    usePrimary = true;
                    paramToUncheck = GetFlangeParamToUncheck(true, isBellows);
                }
                else
                {
                    // FLANGE + Primary 아님 → 그 커넥터를 사용
                    usePrimary = false;
                    chosenConnectorId = only.Id;
                    paramToUncheck = GetFlangeParamToUncheck(false, isBellows);
                }
            }
            else
            {
                // 열린 커넥터가 0개이거나 3개 이상이면 대상이 아님
                return;
            }

            if (isFlange) result.FlangeTargetCount++;
            else result.NutTargetCount++;

            // 3) 파라미터 해제 (형상이 바뀌므로 이후 커넥터는 다시 조회한다)
            if (paramToUncheck != null && ElementUtils.UncheckYesNoParam(part, paramToUncheck))
            {
                result.ParamUncheckedCount++;
                doc.Regenerate();
            }

            // 4) 실제로 연결할 커넥터를 다시 조회
            Connector subConn = usePrimary
                ? ElementUtils.GetPrimaryConnector(doc.GetElement(partId))
                : ElementUtils.ResolveConnector(doc, partId, chosenConnectorId);

            Connector targetConn = ElementUtils.ResolveConnector(doc, target.OwnerId, target.ConnectorId);

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

                if (isFlange) result.FlangeConnectedCount++;
                else result.NutConnectedCount++;
            }
            catch (Exception)
            {
                AddFailed(isFlange, result);
            }
        }

        /// <summary>
        /// FLANGE 에서 해제할 파라미터 이름을 고른다.
        ///
        ///  보통 FLANGE : Primary 이면 "FLANGE 하", 아니면 "FLANGE 상"
        ///  BELLOWS     : 위와 반대로 Primary 이면 "FLANGE 상", 아니면 "FLANGE 하"
        /// </summary>
        /// <param name="isPrimary">Primary 커넥터를 쓰는 경우인지</param>
        /// <param name="isBellows">부품 이름에 BELLOWS 가 들어있는지</param>
        private static string GetFlangeParamToUncheck(bool isPrimary, bool isBellows)
        {
            if (isBellows)
                return isPrimary ? ParamFlangeUpper : ParamFlangeLower;

            return isPrimary ? ParamFlangeLower : ParamFlangeUpper;
        }

        /// <summary>실패 수를 부품 종류에 맞게 증가.</summary>
        private static void AddFailed(bool isFlange, RunResult result)
        {
            if (isFlange) result.FlangeFailedCount++;
            else result.NutFailedCount++;
        }
    }
}
