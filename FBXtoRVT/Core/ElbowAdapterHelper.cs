using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "엘보 어댑터 생성기" 기능의 핵심 로직.
    ///
    /// 대상은 패밀리명에 "ASSEMBLY_ELBOW_ADPT_LOT-FLON" 이 들어간 엘보 조립품이다.
    /// 이 엘보의 <b>배관 쪽 끝</b>에 어댑터가 필요하므로, 그 자리를 찾아 ADAPTOR 파라미터를 켠다.
    ///
    /// 처리 흐름 (엘보 1개 기준)
    ///  1) 엘보의 End 커넥터가 2개이고, <b>둘 다 닫혀(연결돼) 있어야</b> 대상이다.
    ///     (아직 아무것도 안 붙은 엘보는 건드리지 않는다)
    ///  2) 엘보 중심점에서 <b>가장 가까운 SCR 장비</b>를 고른다.
    ///     (SCR 장비가 여러 대일 수 있으므로, 이 엘보가 어느 장비에 딸린 것인지를 먼저 정한다)
    ///  3) 두 커넥터 중 그 장비 중심점에서 <b>더 먼 쪽</b>을 고른다.
    ///     (장비 쪽이 아니라 바깥으로 나가는 쪽이 어댑터가 붙을 자리다)
    ///  4) 그 먼 쪽 커넥터가 <b>배관과 연결돼 있으면</b>, 그 커넥터 쪽 ADAPTOR 파라미터를 켠다(Yes).
    ///     상/하 중 어느 쪽인지는 <see cref="PartSideTable"/> 의 표가 정한다.
    ///     (이 패밀리는 Primary 가 "상" 이므로, 먼 쪽이 Primary 면 ADAPTOR_상, 아니면 ADAPTOR_하)
    ///
    /// CLAMP 파라미터는 건드리지 않는다.
    /// </summary>
    public static class ElbowAdapterHelper
    {
        // 기능 이름 (결과 대화상자 제목 / 로그에 함께 쓴다)
        public const string FeatureName = "엘보 어댑터 생성기";

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int ElbowCount;             // 찾은 대상 엘보 수
            public int ScrubberCount;          // 찾은 SCR 장비 수
            public int CheckedCount;           // ADAPTOR 를 켠 수
            public int AlreadyCheckedCount;    // 이미 켜져 있던 수
            public int SkippedOpenCount;       // 커넥터가 열려 있어서 건너뛴 수
            public int SkippedNotPipeCount;    // 먼 쪽 커넥터가 배관이 아니라 건너뛴 수
            public int SkippedNotTwoCount;     // End 커넥터가 2개가 아니라 건너뛴 수
            public int ParamNotFoundCount;     // ADAPTOR 파라미터를 찾지 못한 수
        }

        /// <summary>
        /// 미리 모아 둔 SCR 장비. 이 기능에서 장비는 움직이지 않으므로 중심점을 한 번만 구해 둔다.
        /// </summary>
        private class ScrubberRef
        {
            public ElementId Id;
            public XYZ Center;
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            // 처리 도중 형상이 바뀌므로, 대상 엘보는 Id 목록으로 먼저 확정해 둔다.
            var elbowIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(
                doc, view, FamilyKeywords.ElbowAdptAssembly))
            {
                elbowIds.Add(fi.Id);
            }
            result.ElbowCount = elbowIds.Count;

            // SCR 장비도 여기서 한 번만 모은다. (규칙: 반복문 안에서 다시 수집하지 않는다)
            List<ScrubberRef> scrubbers = CollectScrubbers(doc, view);
            result.ScrubberCount = scrubbers.Count;

            LogUtils.Log($"===== {FeatureName} 실행 시작. 대상 엘보 {result.ElbowCount}개, " +
                $"SCR 장비 {result.ScrubberCount}대 =====");

            // 기준이 될 장비가 없으면 어느 쪽이 바깥인지 정할 수 없다.
            if (scrubbers.Count == 0)
            {
                LogUtils.Log($"{FeatureName}: 현재 뷰에 SCR 장비가 없어 아무것도 하지 않음.");
                return result;
            }

            foreach (ElementId elbowId in elbowIds)
            {
                ProcessOneElbow(doc, elbowId, scrubbers, result);
            }

            LogUtils.Log($"===== {FeatureName} 실행 종료. 체크={result.CheckedCount} " +
                $"이미체크={result.AlreadyCheckedCount} 열린커넥터={result.SkippedOpenCount} " +
                $"배관아님={result.SkippedNotPipeCount} 커넥터2개아님={result.SkippedNotTwoCount} " +
                $"파라미터없음={result.ParamNotFoundCount} =====");

            return result;
        }

        /// <summary>
        /// 엘보 1개 처리. 조건이 맞지 않으면 아무것도 하지 않는다.
        /// </summary>
        private static void ProcessOneElbow(Document doc, ElementId elbowId,
            List<ScrubberRef> scrubbers, RunResult result)
        {
            var elbow = doc.GetElement(elbowId) as FamilyInstance;
            if (elbow == null) return;

            // 1) End 커넥터 2개 + 둘 다 닫혀(연결돼) 있어야 대상
            List<Connector> conns = ElementUtils.GetEndConnectors(elbow);
            if (conns.Count != 2)
            {
                LogUtils.LogDetail($"엘보(Id={elbowId}) End 커넥터가 {conns.Count}개(2개 아님) -> 건너뜀.");
                result.SkippedNotTwoCount++;
                return;
            }

            if (!conns[0].IsConnected || !conns[1].IsConnected)
            {
                LogUtils.LogDetail($"엘보(Id={elbowId}) 열린 커넥터가 있어 건너뜀.");
                result.SkippedOpenCount++;
                return;
            }

            // 2) 엘보 중심점에서 가장 가까운 SCR 장비를 이 엘보의 기준 장비로 삼는다.
            XYZ elbowCenter = ElementUtils.GetCenter(elbow);
            if (elbowCenter == null)
            {
                LogUtils.LogDetail($"엘보(Id={elbowId}) 중심점을 구하지 못해 건너뜀.");
                result.SkippedNotTwoCount++;
                return;
            }

            ScrubberRef scrubber = FindNearestScrubber(scrubbers, elbowCenter);

            // 3) 두 커넥터 중 그 장비에서 더 먼 쪽 (거리가 같으면 앞의 것)
            double dist0 = conns[0].Origin.DistanceTo(scrubber.Center);
            double dist1 = conns[1].Origin.DistanceTo(scrubber.Center);
            Connector farConn = (dist0 >= dist1) ? conns[0] : conns[1];

            // 4) 먼 쪽 커넥터가 배관과 연결돼 있어야 어댑터를 켠다.
            if (!IsConnectedToPipe(farConn, elbowId))
            {
                if (LogUtils.DetailEnabled)
                    LogUtils.LogDetail($"엘보(Id={elbowId}) 먼 쪽 커넥터(Id={farConn.Id})가 배관이 아님 -> 건너뜀. " +
                        $"기준장비 Id={scrubber.Id} 거리={dist0:F3}/{dist1:F3}ft");

                result.SkippedNotPipeCount++;
                return;
            }

            // 5) 그 커넥터 쪽 ADAPTOR 파라미터를 켠다. (상/하 판단은 패밀리별 표가 한다)
            bool isPrimary = ElementUtils.IsPrimaryConnector(farConn);
            PartSide side = PartSideTable.GetSideOfConnector(elbow, isPrimary);
            string paramName = PartSideTable.AdaptorPair.Get(side);

            if (paramName == null)
            {
                LogUtils.Log($"엘보(Id={elbowId}, Family={ElementUtils.GetFamilyName(elbow)}) 이(가) " +
                    $"{nameof(PartSideTable)} 표에 없어 어느 쪽이 상/하인지 알 수 없음 -> 건너뜀.");
                result.ParamNotFoundCount++;
                return;
            }

            if (!ElementUtils.HasWritableYesNoParam(elbow, paramName))
            {
                LogUtils.Log($"엘보(Id={elbowId}) 에 '{paramName}' YES/NO 파라미터가 없거나 읽기전용 -> 건너뜀.");
                result.ParamNotFoundCount++;
                return;
            }

            if (LogUtils.DetailEnabled)
                LogUtils.LogDetail($"엘보(Id={elbowId}) 기준장비 Id={scrubber.Id} " +
                    $"먼쪽 커넥터 Id={farConn.Id} Primary={isPrimary} 쪽={side} -> '{paramName}' 체크");

            if (ElementUtils.SetYesNoParam(elbow, paramName, true))
            {
                result.CheckedCount++;
                doc.Regenerate(); // 형상이 바뀌므로 곧바로 반영한다
            }
            else
            {
                result.AlreadyCheckedCount++;
            }
        }

        /// <summary>
        /// 현재 뷰의 SCR 장비를 한 번만 수집한다. (중심점까지 미리 구해 둔다)
        /// </summary>
        private static List<ScrubberRef> CollectScrubbers(Document doc, View view)
        {
            var list = new List<ScrubberRef>();

            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Scrubber))
            {
                XYZ center = ElementUtils.GetCenter(fi);
                if (center == null) continue;

                list.Add(new ScrubberRef { Id = fi.Id, Center = center });
            }

            return list;
        }

        /// <summary>
        /// 기준점에 가장 가까운 SCR 장비를 반환. (목록이 비어 있지 않을 때만 부른다)
        /// </summary>
        private static ScrubberRef FindNearestScrubber(List<ScrubberRef> scrubbers, XYZ target)
        {
            ScrubberRef best = null;
            double bestDist = double.MaxValue;

            foreach (ScrubberRef s in scrubbers)
            {
                double dist = s.Center.DistanceTo(target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// 이 커넥터에 배관(Pipe)이 이어져 있는지 검사.
        /// 자기 자신과 논리적(System) 참조는 빼고, 실제로 맞물린 End 커넥터의 주인만 본다.
        /// </summary>
        private static bool IsConnectedToPipe(Connector c, ElementId selfId)
        {
            foreach (Connector other in c.AllRefs)
            {
                if (other.ConnectorType != ConnectorType.End) continue;   // 논리적 참조 제외

                Element owner = other.Owner;
                if (owner == null) continue;
                if (owner.Id == selfId) continue;                         // 자기 자신

                if (owner is Pipe) return true;
            }

            return false;
        }
    }
}
