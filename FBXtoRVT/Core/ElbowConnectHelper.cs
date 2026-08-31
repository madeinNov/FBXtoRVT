using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "ELBOW&amp;배관/플랜지" 기능의 핵심 로직.
    ///
    /// 처리 흐름 (엘보의 열린 커넥터 1개 기준)
    ///  1) 현재 뷰에서 패밀리명에 "ELBOW" 가 포함되고 열린 커넥터가 있는 객체를 모은다.
    ///  2) 엘보의 열린 커넥터 원점을 중심으로 하는 한 변 60mm 짜리 탐색 박스를 만든다.
    ///  3) 탐색 박스 안에 무엇이 들어있는지에 따라 처리한다.
    ///
    ///     (1) FLANGE 만 있는 경우
    ///         플랜지를 이동·회전시켜 엘보의 열린 커넥터에 연결한다.
    ///         (플랜지에 열린 커넥터가 1개 이상 있을 때만 수행)
    ///
    ///     (2) 배관 끝점(그 자리의 커넥터가 열린 경우)만 있는 경우
    ///         엘보를 이동·회전시켜 배관 커넥터에 연결한다.
    ///
    ///     (3) 둘 다 있는 경우
    ///         (1) 을 먼저 수행한 뒤, 플랜지의 반대쪽 커넥터가 열려 있으면
    ///         그 커넥터를 배관 커넥터에 연결한다. (이때는 아무것도 움직이지 않고 연결만 시도)
    /// </summary>
    public static class ElbowConnectHelper
    {
        // 탐색 박스 한 변의 길이(mm). 커넥터 원점을 중심으로 ±30mm 범위가 된다.
        private const double SearchBoxSizeMm = 60.0;

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int ElbowCount;            // 열린 커넥터가 있는 엘보 수
            public int FlangeConnectedCount;  // 엘보에 붙인 플랜지 수
            public int PipeConnectedCount;    // 배관에 연결한 수 (엘보 직접 + 플랜지 경유)
            public int FailedCount;           // 연결 시도했으나 실패한 수
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            double boxSize = ElementUtils.MmToFeet(SearchBoxSizeMm);

            // 1) 처리 대상 목록을 먼저 확정한다. (처리 도중 객체가 이동하므로 Id 로 보관)
            var elbowIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Elbow))
            {
                if (ElementUtils.GetOpenEndConnectors(fi).Count == 0) continue;
                elbowIds.Add(fi.Id);
            }
            result.ElbowCount = elbowIds.Count;

            var flangeIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Flange))
            {
                flangeIds.Add(fi.Id);
            }

            // 배관은 이 기능에서 움직이지 않으므로, 열린 커넥터 좌표를 한 번만 모아 둔다.
            List<ConnRef> pipeConns = CollectOpenPipeConnectors(doc, view);

            LogUtils.Log($"===== ELBOW&배관/플랜지 실행 시작. 엘보 {result.ElbowCount}개, " +
                $"FLANGE 후보 {flangeIds.Count}개, 열린 배관 커넥터 {pipeConns.Count}개 =====");

            // 이미 쓴 플랜지 / 배관 커넥터는 다시 쓰지 않도록 기록
            var usedFlangeIds = new HashSet<ElementId>();
            var usedPipeConnKeys = new HashSet<string>();

            // 2) 엘보 하나씩, 그 엘보의 열린 커넥터 하나씩 처리
            foreach (ElementId elbowId in elbowIds)
            {
                var elbow = doc.GetElement(elbowId) as FamilyInstance;
                if (elbow == null) continue;

                // 처리 도중 커넥터가 닫히므로, 커넥터 Id 목록을 먼저 확정한다.
                var openConnIds = new List<int>();
                foreach (Connector c in ElementUtils.GetOpenEndConnectors(elbow))
                {
                    openConnIds.Add(c.Id);
                }

                foreach (int connId in openConnIds)
                {
                    ProcessOneElbowConnector(doc, elbowId, connId, boxSize,
                        flangeIds, usedFlangeIds, pipeConns, usedPipeConnKeys, result);
                }
            }

            LogUtils.Log($"===== ELBOW&배관/플랜지 실행 종료. 붙인 FLANGE={result.FlangeConnectedCount} " +
                $"배관연결={result.PipeConnectedCount} 실패={result.FailedCount} =====");

            return result;
        }

        /// <summary>
        /// 엘보의 열린 커넥터 1개에 대한 처리.
        /// </summary>
        private static void ProcessOneElbowConnector(Document doc, ElementId elbowId, int elbowConnId,
            double boxSize, List<ElementId> flangeIds, HashSet<ElementId> usedFlangeIds,
            List<ConnRef> pipeConns, HashSet<string> usedPipeConnKeys, RunResult result)
        {
            Connector elbowConn = ElementUtils.ResolveConnector(doc, elbowId, elbowConnId);
            if (elbowConn == null || elbowConn.IsConnected) return; // 그 사이 닫혔으면 대상 아님

            // 커넥터 원점을 중심으로 하는 60mm 탐색 박스
            ElementUtils.WorldBox searchBox = ElementUtils.WorldBox.FromCenter(elbowConn.Origin, boxSize);
            XYZ elbowConnOrigin = elbowConn.Origin;

            // 박스 안의 플랜지 / 배관 커넥터 후보 찾기 (각각 가장 가까운 것 1개)
            ElementId flangeId = FindFlangeInBox(doc, flangeIds, usedFlangeIds, searchBox, elbowConnOrigin);
            ConnRef pipeConn = FindPipeConnectorInBox(pipeConns, usedPipeConnKeys, searchBox, elbowConnOrigin);

            if (flangeId != null)
            {
                // (1)(3) 플랜지를 엘보에 연결 (플랜지가 이동·회전)
                int flangeUsedConnId = ConnectFlangeToElbow(doc, elbowId, elbowConnId, flangeId, result);
                if (flangeUsedConnId < 0) return;

                usedFlangeIds.Add(flangeId);

                // (3) 배관도 함께 있었다면, 플랜지의 반대쪽 커넥터를 배관에 연결
                if (pipeConn != null)
                {
                    ConnectFlangeOppositeToPipe(doc, flangeId, flangeUsedConnId, pipeConn, usedPipeConnKeys, result);
                }
            }
            else if (pipeConn != null)
            {
                // (2) 플랜지가 없으면 엘보 자체를 배관에 연결 (엘보가 이동·회전)
                ConnectElbowToPipe(doc, elbowId, elbowConnId, pipeConn, usedPipeConnKeys, result);
            }
        }

        /// <summary>
        /// 플랜지를 엘보의 열린 커넥터에 이동·회전으로 연결한다.
        /// 성공하면 플랜지 쪽에서 사용한 커넥터 Id 를, 실패하면 -1 을 반환한다.
        /// </summary>
        private static int ConnectFlangeToElbow(Document doc, ElementId elbowId, int elbowConnId,
            ElementId flangeId, RunResult result)
        {
            Connector elbowConn = ElementUtils.ResolveConnector(doc, elbowId, elbowConnId);
            if (elbowConn == null || elbowConn.IsConnected) return -1;

            // 플랜지의 열린 커넥터 중 엘보 커넥터에 가장 가까운 것을 사용
            Connector flangeConn = ElementUtils.FindNearestOpenEndConnector(doc.GetElement(flangeId), elbowConn.Origin);
            if (flangeConn == null) return -1; // 열린 커넥터가 없으면 대상 아님

            int flangeConnId = flangeConn.Id;

            try
            {
                // 엘보 커넥터를 기준(Main)으로 두고, 플랜지(Sub)를 움직여 맞춘다.
                ConnectorHelper.AlignAndConnect(doc, elbowConn, flangeConn, flangeId);
                doc.Regenerate();

                result.FlangeConnectedCount++;
                return flangeConnId;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"FLANGE(Id={flangeId}) 를 엘보(Id={elbowId}) 에 붙이지 못함.");
                result.FailedCount++;
                return -1;
            }
        }

        /// <summary>
        /// 엘보에 붙인 플랜지의 "반대쪽" 커넥터를 배관 커넥터에 연결한다.
        /// 플랜지는 이미 엘보에 고정됐으므로 아무것도 움직이지 않고 연결만 시도한다.
        /// (두 커넥터 위치가 맞지 않으면 Revit 이 연결을 거부한다)
        /// </summary>
        private static void ConnectFlangeOppositeToPipe(Document doc, ElementId flangeId, int usedConnId,
            ConnRef pipeConn, HashSet<string> usedPipeConnKeys, RunResult result)
        {
            Element flange = doc.GetElement(flangeId);
            if (flange == null) return;

            // 방금 엘보에 쓴 커넥터가 아닌, 남아있는 열린 커넥터를 찾는다.
            Connector opposite = null;
            foreach (Connector c in ElementUtils.GetOpenEndConnectors(flange))
            {
                if (c.Id == usedConnId) continue;
                opposite = c;
                break;
            }

            if (opposite == null) return; // 반대쪽이 닫혀 있으면 아무것도 하지 않음

            Connector target = pipeConn.Resolve(doc);
            if (target == null || target.IsConnected) return;

            try
            {
                target.ConnectTo(opposite);
                usedPipeConnKeys.Add(pipeConn.Key);
                result.PipeConnectedCount++;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"FLANGE(Id={flangeId}) 반대쪽 커넥터를 배관({pipeConn.Key}) 에 연결하지 못함.");
                result.FailedCount++;
            }
        }

        /// <summary>
        /// 엘보를 배관 커넥터에 이동·회전으로 연결한다. (엘보가 움직인다)
        /// </summary>
        private static void ConnectElbowToPipe(Document doc, ElementId elbowId, int elbowConnId,
            ConnRef pipeConn, HashSet<string> usedPipeConnKeys, RunResult result)
        {
            Connector elbowConn = ElementUtils.ResolveConnector(doc, elbowId, elbowConnId);
            Connector target = pipeConn.Resolve(doc);

            if (elbowConn == null || elbowConn.IsConnected) return;
            if (target == null || target.IsConnected) return;

            try
            {
                // 배관 커넥터를 기준(Main)으로 두고, 엘보(Sub)를 움직여 맞춘다.
                ConnectorHelper.AlignAndConnect(doc, target, elbowConn, elbowId);
                doc.Regenerate();

                usedPipeConnKeys.Add(pipeConn.Key);
                result.PipeConnectedCount++;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"엘보(Id={elbowId}) 를 배관({pipeConn.Key}) 에 연결하지 못함.");
                result.FailedCount++;
            }
        }

        /// <summary>
        /// 탐색 박스 안에 있는 플랜지 중 엘보 커넥터에 가장 가까운 것의 Id 를 반환. 없으면 null.
        ///
        /// "박스 안에 있다" 는 판정은
        ///  - 플랜지의 열린 커넥터가 박스 안에 있거나
        ///  - 플랜지의 중심점이 박스 안에 있거나
        /// 둘 중 하나면 성립하는 것으로 본다.
        /// (플랜지 길이에 따라 커넥터와 중심점 중 어느 쪽이 박스에 들어올지 달라지기 때문)
        /// </summary>
        private static ElementId FindFlangeInBox(Document doc, List<ElementId> flangeIds,
            HashSet<ElementId> usedFlangeIds, ElementUtils.WorldBox searchBox, XYZ elbowConnOrigin)
        {
            ElementId best = null;
            double bestDist = double.MaxValue;

            foreach (ElementId id in flangeIds)
            {
                if (usedFlangeIds.Contains(id)) continue;

                Element flange = doc.GetElement(id);
                if (flange == null) continue;

                // 열린 커넥터가 1개 이상 있어야 연결 대상
                List<Connector> openConns = ElementUtils.GetOpenEndConnectors(flange);
                if (openConns.Count == 0) continue;

                // 박스 안에 들어온 지점 중 엘보 커넥터에 가장 가까운 거리 계산
                double dist = double.MaxValue;

                foreach (Connector c in openConns)
                {
                    if (!searchBox.Contains(c.Origin)) continue;
                    dist = Math.Min(dist, c.Origin.DistanceTo(elbowConnOrigin));
                }

                XYZ center = ElementUtils.GetCenter(flange);
                if (center != null && searchBox.Contains(center))
                {
                    dist = Math.Min(dist, center.DistanceTo(elbowConnOrigin));
                }

                if (dist == double.MaxValue) continue; // 박스 밖

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = id;
                }
            }

            return best;
        }

        /// <summary>
        /// 탐색 박스 안에 있는 배관 열린 커넥터 중 엘보 커넥터에 가장 가까운 것을 반환. 없으면 null.
        /// </summary>
        private static ConnRef FindPipeConnectorInBox(List<ConnRef> pipeConns, HashSet<string> usedPipeConnKeys,
            ElementUtils.WorldBox searchBox, XYZ elbowConnOrigin)
        {
            ConnRef best = null;
            double bestDist = double.MaxValue;

            foreach (ConnRef c in pipeConns)
            {
                if (usedPipeConnKeys.Contains(c.Key)) continue;
                if (!searchBox.Contains(c.Origin)) continue;

                double dist = c.Origin.DistanceTo(elbowConnOrigin);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// 현재 뷰의 배관에서 열린 End 커넥터(= 배관 끝점)를 모두 모은다.
        /// </summary>
        private static List<ConnRef> CollectOpenPipeConnectors(Document doc, View view)
        {
            var list = new List<ConnRef>();

            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe));

            foreach (Element e in collector)
            {
                var pipe = e as Pipe;
                if (pipe == null) continue;

                foreach (Connector c in ElementUtils.GetOpenEndConnectors(pipe))
                {
                    list.Add(ConnRef.From(pipe.Id, c));
                }
            }

            return list;
        }
    }
}
