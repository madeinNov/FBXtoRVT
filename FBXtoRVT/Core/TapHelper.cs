using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "TAP" 기능의 핵심 로직.
    ///
    /// [무엇을 하는 기능인가]
    /// FBX 에서 넘어온 "탭 분기" 자리를 정리해서 Revit 배관으로 이어 준다.
    /// 사용자는 <b>커넥터 2개짜리 Pipe Fitting</b>(세로로 서 있는 분기 부품)을 선택한다. (여러 개 가능)
    /// 선택한 피팅을 중심으로 한 변 120mm 정육면체 박스를 만들고, 그 안에서 아래를 찾는다.
    ///   - 커넥터 1개짜리 Pipe Fitting (캡)           : 여러 개면 선택 객체에 가장 가까운 것
    ///   - 서로 다른 배관의 끝 커넥터 (직선배관 두 토막) : 여러 개면 선택 객체에 가까운 순서로 2개
    ///
    /// [처리 순서]  (선택한 피팅 하나마다 아래를 반복한다)
    ///  1) 피팅의 커넥터 2개에서 각각 33mm 배관을 만든다. ← 이것만은 반드시 한다.
    ///     배관 타입은 피팅 패밀리명을 '_' 로 나눈 마지막 단어("STS316L BA" / "STS316L EP")를 쓰고,
    ///     지름은 커넥터 지름, System Type 은 직선배관을 따라간다.
    ///  2) 두 배관을 하나의 직선배관으로 합친다. <b>긴 배관이 남고 짧은 배관이 지워진다.</b>
    ///     (긴 배관을 짧은 배관의 반대쪽 끝까지 늘린다. 짧은 배관 반대쪽에 붙어 있던 객체는 다시 붙인다)
    ///  3) 캡(1커넥터 피팅)의 규격(ND1)을 붙을 배관의 ND 와 같게 맞춘 뒤,
    ///     두 33mm 배관 중 캡에 가까운 쪽 끝에 이동·회전시켜 붙인다.
    ///  4) 피팅 + 33mm 배관 2개 + 캡을 <b>한 덩어리로</b> 돌려서, 캡이 없는 쪽 커넥터가 직선배관을 향하게 한다.
    ///     (피팅 중심에서 직선배관에 내린 수선 방향으로 맞춘다. 이미 향해 있으면 돌리지 않는다)
    ///  5) 캡이 없는 쪽 33mm 배관의 끝을 직선배관 중심선까지 늘리고 <b>탭(Takeoff)</b> 으로 붙인다.
    ///
    /// 어느 단계가 불가능해도(캡이 없음 / 배관이 없음 / 탭 실패 등) <b>그 전 단계까지는 그대로 남긴다.</b>
    /// 1) 의 배관 생성조차 안 되는 경우에만 그 피팅을 통째로 건너뛴다(롤백).
    ///
    /// [실패 처리]
    /// 대화상자를 띄우지 않는다. 처리 내용과 건너뛴 이유는 로그에만 남긴다.
    /// </summary>
    public static class TapHelper
    {
        // 선택 객체를 중심으로 만드는 정육면체 검색 박스의 한 변 길이 (mm)
        private const double SearchBoxSizeMm = 120.0;

        // 피팅 커넥터에서 만드는 배관 길이 (mm)
        private const double BranchPipeLengthMm = 33.0;

        // 캡(1커넥터 피팅)의 규격(ND)을 정하는 인스턴스 파라미터 이름
        private const string CapSizeParamName = "ND1";

        // 패밀리명 마지막 단어로 허용하는 배관 타입 이름 (정확히 일치해야 한다)
        private static readonly string[] AllowedPipeTypeNames = { "STS316L BA", "STS316L EP" };

        // 피팅이 "이미 직선배관을 향해 있다" 고 볼 각도 허용오차(라디안). 약 1도.
        private const double AlreadyFacingTolerance = 0.0175;

        // 위치가 "같다" 고 볼 오차 (0.1mm)
        private static readonly double PositionTolerance = ElementUtils.MmToFeet(0.1);

        /// <summary>
        /// 실행 결과 요약. (창은 띄우지 않지만 로그와 롤백 판단에 쓴다)
        /// </summary>
        public class TapResult
        {
            /// <summary>배관 생성까지는 된 객체 수 (이후 단계는 일부만 됐을 수 있다)</summary>
            public int ProcessedCount;

            /// <summary>배관 생성조차 못 해 건너뛴 객체 수</summary>
            public int SkippedCount;
        }

        /// <summary>
        /// 현재 뷰에서 한 번만 모아 두는 후보 목록. (선택 객체가 여러 개여도 수집은 한 번만)
        /// 처리 도중 지워지는 객체가 있을 수 있으므로 Id 로 들고 있다가 쓸 때 다시 꺼낸다.
        /// </summary>
        private class Candidates
        {
            public List<ElementId> OneConnFittingIds = new List<ElementId>();
            public List<ElementId> TwoConnFittingIds = new List<ElementId>();
            public List<ElementId> PipeIds = new List<ElementId>();
        }

        /// <summary>
        /// 박스 안에 들어온 배관 끝 커넥터 하나. (배관 하나당 하나만 담는다)
        /// </summary>
        private class PipeEnd
        {
            public ElementId PipeId;
            public int ConnectorId;
            public XYZ Origin;
            public double DistToCenter;   // 선택 객체 중심까지 거리 (가까운 순서로 고르는 데 쓴다)
        }

        /// <summary>
        /// 선택 객체 하나하나에 TAP 을 적용한다. (외부에서 Transaction 을 열고 호출)
        /// 객체마다 SubTransaction 으로 감싸서, 하나가 실패해도 나머지는 계속 진행한다.
        /// </summary>
        public static TapResult Run(Document doc, View view, ICollection<ElementId> selectedIds)
        {
            var result = new TapResult();
            Candidates cands = CollectCandidates(doc, view);

            LogUtils.Log($"===== TAP 시작. 선택 {selectedIds.Count}개 / 후보: 1커넥터 피팅 {cands.OneConnFittingIds.Count}, " +
                $"2커넥터 피팅 {cands.TwoConnFittingIds.Count}, 배관 {cands.PipeIds.Count} =====");

            // 같은 피팅을 두 번 처리하지 않도록 기억한다.
            var processedFittingIds = new HashSet<ElementId>();

            foreach (ElementId selectedId in selectedIds)
            {
                Element selected = doc.GetElement(selectedId);
                if (selected == null)
                {
                    result.SkippedCount++;
                    continue;
                }

                using (SubTransaction sub = new SubTransaction(doc))
                {
                    sub.Start();

                    bool ok = false;
                    try
                    {
                        ok = ProcessOne(doc, cands, selected, processedFittingIds);
                    }
                    catch (Exception ex)
                    {
                        LogUtils.LogError(ex, $"TAP 처리 중 오류. 선택 객체 Id={selectedId}");
                        ok = false;
                    }

                    if (ok)
                    {
                        sub.Commit();
                        result.ProcessedCount++;
                    }
                    else
                    {
                        sub.RollBack();
                        result.SkippedCount++;
                    }
                }
            }

            LogUtils.Log($"===== TAP 종료. 처리={result.ProcessedCount} 건너뜀={result.SkippedCount} =====");
            return result;
        }

        // ===== 후보 수집 =====

        /// <summary>
        /// 현재 뷰의 Pipe Fitting(커넥터 개수별) 과 배관을 모은다. 복합 패밀리의 Sub-Component 는 제외.
        /// </summary>
        private static Candidates CollectCandidates(Document doc, View view)
        {
            var cands = new Candidates();

            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstancesByCategory(doc, view, BuiltInCategory.OST_PipeFitting))
            {
                int connCount = ElementUtils.GetEndConnectors(fi).Count;

                if (connCount == 1) cands.OneConnFittingIds.Add(fi.Id);
                else if (connCount == 2) cands.TwoConnFittingIds.Add(fi.Id);
            }

            var pipeCollector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe));

            foreach (Element e in pipeCollector)
                cands.PipeIds.Add(e.Id);

            return cands;
        }

        // ===== 객체 하나 처리 =====

        /// <summary>
        /// 선택 객체 하나에 대해 TAP 을 적용한다.
        /// 배관 생성(1단계)까지 됐으면 true. 그것도 못 하면 false 를 돌려주고 호출한 쪽에서 롤백한다.
        /// </summary>
        private static bool ProcessOne(Document doc, Candidates cands, Element selected, HashSet<ElementId> processedFittingIds)
        {
            // ===== 0) 선택 객체 중심의 120mm 정육면체 박스 =====
            XYZ center = ElementUtils.GetCenter(selected);
            if (center == null)
            {
                LogUtils.Log($"  선택 객체(Id={selected.Id})의 중심점을 얻지 못해 건너뜁니다.");
                return false;
            }

            ElementUtils.WorldBox box = ElementUtils.WorldBox.FromCenter(center, ElementUtils.MmToFeet(SearchBoxSizeMm));

            // ===== 0-1) 2커넥터 피팅: 선택 객체 자신이면 그것, 아니면 박스 안에서 가장 가까운 것 =====
            FamilyInstance fitting = ResolveTwoConnFitting(doc, cands, selected, box, center);
            if (fitting == null)
            {
                LogUtils.Log($"  선택 객체(Id={selected.Id}) 는 2커넥터 피팅이 아니고 주변에도 없어 건너뜁니다.");
                return false;
            }

            if (processedFittingIds.Contains(fitting.Id))
            {
                LogUtils.Log($"  2커넥터 피팅(Id={fitting.Id})은 이미 처리해서 건너뜁니다.");
                return false;
            }

            List<Connector> fittingConns = ElementUtils.GetEndConnectors(fitting);
            if (fittingConns.Count != 2 || fittingConns.Any(c => c.IsConnected))
            {
                LogUtils.Log($"  2커넥터 피팅(Id={fitting.Id})의 커넥터가 모두 열려 있지 않아 배관을 만들 수 없어 건너뜁니다.");
                return false;
            }

            // ===== 0-2) 주변 후보: 캡(가장 가까운 1개) / 배관 끝(가까운 순 2개) =====
            FamilyInstance cap = FindNearestOpenCap(doc, cands.OneConnFittingIds, box, center, fitting.Id);
            List<PipeEnd> pipeEnds = FindNearestPipeEnds(doc, cands.PipeIds, box, center, 2);

            LogUtils.Log($"  --- TAP 처리. 피팅 Id={fitting.Id} 캡={(cap != null ? cap.Id.ToString() : "없음")} " +
                $"배관={string.Join(",", pipeEnds.Select(p => p.PipeId.ToString()))} ---");

            // ===== 0-3) 배관 타입 / System Type =====
            Pipe referencePipe = (pipeEnds.Count > 0) ? doc.GetElement(pipeEnds[0].PipeId) as Pipe : null;

            PipeType pipeType = ResolvePipeType(doc, fitting, referencePipe);
            if (pipeType == null)
            {
                LogUtils.Log("  쓸 배관 타입을 정하지 못해 건너뜁니다.");
                return false;
            }

            ElementId systemTypeId = ResolveSystemTypeId(doc, referencePipe, fitting);
            ElementId levelId = GetLevelId(doc, referencePipe, center);

            ElementId fittingId = fitting.Id;

            // ===== 1) 피팅 커넥터 2개에서 33mm 배관 생성 (반드시) =====
            ElementId branchAId = CreateBranchPipe(doc, fittingId, fittingConns[0].Id, pipeType, systemTypeId, levelId);
            ElementId branchBId = CreateBranchPipe(doc, fittingId, fittingConns[1].Id, pipeType, systemTypeId, levelId);

            if (branchAId == null || branchBId == null)
            {
                LogUtils.Log("  33mm 배관을 만들지 못해 건너뜁니다.");
                return false;
            }

            processedFittingIds.Add(fittingId);

            // 여기서부터는 어느 단계가 안 되더라도 그 전까지의 결과는 남긴다.

            // ===== 2) 배관 통합: 긴 배관이 남고 짧은 배관이 지워진다 =====
            ElementId basePipeId = null;

            if (pipeEnds.Count == 2)
            {
                basePipeId = TryMergePipes(doc, pipeEnds[0], pipeEnds[1]);
            }
            else if (pipeEnds.Count == 1)
            {
                basePipeId = pipeEnds[0].PipeId;   // 합칠 상대가 없으면 그 배관을 직선배관으로 본다
                LogUtils.Log($"  주변 배관이 1개뿐이라 합치지 않고 Id={basePipeId} 를 직선배관으로 씁니다.");
            }
            else
            {
                LogUtils.Log("  주변에 배관이 없어 통합 / 회전 / 탭 단계를 건너뜁니다.");
            }

            // ===== 3) 캡을 33mm 배관 끝에 붙인다 (캡에 가까운 쪽) =====
            ElementId cappedBranchId = null;

            if (cap != null)
                cappedBranchId = TryAttachCap(doc, cap.Id, branchAId, branchBId);
            else
                LogUtils.Log("  주변에 캡(1커넥터 피팅)이 없어 캡 단계를 건너뜁니다.");

            if (basePipeId == null) return true;

            // 탭을 넣을 쪽 = 캡이 붙지 않은 쪽 33mm 배관. 캡이 없으면 직선배관에 가까운 쪽.
            ElementId nearBranchId = (cappedBranchId != null)
                ? ((cappedBranchId == branchAId) ? branchBId : branchAId)
                : PickBranchCloserToPipe(doc, basePipeId, branchAId, branchBId);

            if (nearBranchId == null) return true;

            // ===== 4) 피팅 + 33mm 배관 2개 + 캡을 한 덩어리로 돌려 직선배관을 향하게 한다 =====
            var groupIds = new List<ElementId> { fittingId, branchAId, branchBId };
            if (cappedBranchId != null) groupIds.Add(cap.Id);

            if (!TryRotateGroupTowardPipe(doc, fittingId, nearBranchId, basePipeId, groupIds))
                return true;

            // ===== 5) 캡이 없는 쪽 33mm 배관을 직선배관까지 늘리고 탭으로 붙인다 =====
            TryTapIntoPipe(doc, nearBranchId, fittingId, basePipeId);

            return true;
        }

        // ===== 0) 후보 찾기 =====

        /// <summary>
        /// 처리할 2커넥터 피팅을 정한다.
        /// 선택 객체 자신이 커넥터 2개짜리 Pipe Fitting 이면 그것을, 아니면 박스 안에서 중심에 가장 가까운 것을 쓴다.
        /// </summary>
        private static FamilyInstance ResolveTwoConnFitting(Document doc, Candidates cands, Element selected,
            ElementUtils.WorldBox box, XYZ center)
        {
            if (selected is FamilyInstance selectedFi && cands.TwoConnFittingIds.Contains(selectedFi.Id))
                return selectedFi;

            FamilyInstance best = null;
            double bestDist = double.MaxValue;

            foreach (ElementId id in cands.TwoConnFittingIds)
            {
                var fi = doc.GetElement(id) as FamilyInstance;
                if (fi == null) continue;

                XYZ c = ElementUtils.GetCenter(fi);
                if (!box.Contains(c)) continue;

                double dist = c.DistanceTo(center);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = fi;
                }
            }

            return best;
        }

        /// <summary>
        /// 박스 안에 중심점이 들어오고 커넥터가 열려 있는 1커넥터 피팅 중 중심에 가장 가까운 것. 없으면 null.
        /// </summary>
        private static FamilyInstance FindNearestOpenCap(Document doc, List<ElementId> capIds,
            ElementUtils.WorldBox box, XYZ center, ElementId excludeId)
        {
            FamilyInstance best = null;
            double bestDist = double.MaxValue;

            foreach (ElementId id in capIds)
            {
                if (id == excludeId) continue;

                var fi = doc.GetElement(id) as FamilyInstance;
                if (fi == null) continue;   // 앞선 처리에서 지워졌을 수 있다

                XYZ c = ElementUtils.GetCenter(fi);
                if (!box.Contains(c)) continue;

                if (ElementUtils.GetOpenEndConnectors(fi).Count != 1) continue;   // 이미 어딘가에 붙어 있음

                double dist = c.DistanceTo(center);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = fi;
                }
            }

            return best;
        }

        /// <summary>
        /// 열린 끝 커넥터가 박스 안에 들어오는 배관들을 중심에 가까운 순서로 최대 <paramref name="maxCount"/>개 찾는다.
        /// 한 배관의 양 끝이 모두 박스 안이면 중심에 가까운 쪽 하나만 쓴다.
        /// </summary>
        private static List<PipeEnd> FindNearestPipeEnds(Document doc, List<ElementId> pipeIds,
            ElementUtils.WorldBox box, XYZ center, int maxCount)
        {
            var list = new List<PipeEnd>();

            foreach (ElementId id in pipeIds)
            {
                var pipe = doc.GetElement(id) as Pipe;
                if (pipe == null) continue;   // 앞선 처리에서 지워졌을 수 있다

                PipeEnd best = null;

                foreach (Connector c in ElementUtils.GetEndConnectors(pipe))
                {
                    if (c.IsConnected) continue;
                    if (!box.Contains(c.Origin)) continue;

                    double dist = c.Origin.DistanceTo(center);
                    if (best == null || dist < best.DistToCenter)
                        best = new PipeEnd { PipeId = id, ConnectorId = c.Id, Origin = c.Origin, DistToCenter = dist };
                }

                if (best != null) list.Add(best);
            }

            return list.OrderBy(p => p.DistToCenter).Take(maxCount).ToList();
        }

        /// <summary>
        /// 쓸 배관 타입을 정한다.
        ///  1) 2커넥터 피팅의 패밀리명을 '_' 로 나눈 마지막 단어와 이름이 정확히 같은 배관 타입
        ///  2) 그게 안 되면(허용 목록에 없거나 문서에 없음) 주변 배관의 타입
        /// 둘 다 없으면 null.
        /// </summary>
        private static PipeType ResolvePipeType(Document doc, FamilyInstance fitting, Pipe referencePipe)
        {
            string familyName = (fitting.Symbol != null && fitting.Symbol.Family != null)
                ? (fitting.Symbol.Family.Name ?? "")
                : "";

            string[] tokens = familyName.Split('_');
            string lastWord = tokens[tokens.Length - 1].Trim();

            if (AllowedPipeTypeNames.Contains(lastWord))
            {
                PipeType byName = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .FirstOrDefault(t => t.Name == lastWord);

                if (byName != null) return byName;

                LogUtils.Log($"  문서에 배관 타입 '{lastWord}' 이(가) 없습니다.");
            }
            else
            {
                LogUtils.Log($"  피팅(Id={fitting.Id}) 패밀리명 '{familyName}' 의 마지막 단어 '{lastWord}' 는 허용된 배관 타입 이름이 아닙니다.");
            }

            // 이름으로 못 정하면 주변 배관의 타입을 따라간다.
            if (referencePipe != null)
            {
                var fallback = doc.GetElement(referencePipe.GetTypeId()) as PipeType;
                if (fallback != null)
                {
                    LogUtils.Log($"  대신 주변 배관(Id={referencePipe.Id})의 배관 타입 '{fallback.Name}' 을 씁니다.");
                    return fallback;
                }
            }

            return null;
        }

        /// <summary>
        /// 새 배관의 System Type 을 정한다.
        ///  1) 주변 직선배관의 System Type
        ///  2) 피팅 자신의 System Type
        ///  3) 문서의 Undefined(미지정) 배관 시스템 타입 (없으면 아무거나)
        /// </summary>
        private static ElementId ResolveSystemTypeId(Document doc, Pipe referencePipe, FamilyInstance fitting)
        {
            ElementId id = GetSystemTypeId(referencePipe);
            if (id != ElementId.InvalidElementId) return id;

            id = GetSystemTypeId(fitting);
            if (id != ElementId.InvalidElementId) return id;

            var systemTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .ToList();

            PipingSystemType undefined = systemTypes.FirstOrDefault(
                t => t.SystemClassification == MEPSystemClassification.UndefinedSystemClassification)
                ?? systemTypes.FirstOrDefault();

            return (undefined != null) ? undefined.Id : ElementId.InvalidElementId;
        }

        // ===== 1) 33mm 배관 생성 =====

        /// <summary>
        /// 피팅의 지정한 커넥터에서 바깥쪽으로 33mm 배관을 만들고 커넥터에 연결한다.
        /// 지름은 커넥터 지름을 따른다. 실패하면 null.
        /// </summary>
        private static ElementId CreateBranchPipe(Document doc, ElementId fittingId, int connId,
            PipeType pipeType, ElementId systemTypeId, ElementId levelId)
        {
            Connector conn = ElementUtils.ResolveConnector(doc, fittingId, connId);
            if (conn == null) return null;

            XYZ start = conn.Origin;
            XYZ outward = conn.CoordinateSystem.BasisZ.Normalize();
            XYZ end = start + outward * ElementUtils.MmToFeet(BranchPipeLengthMm);
            double diameter = conn.Radius * 2.0;

            try
            {
                Pipe pipe = Pipe.Create(doc, systemTypeId, pipeType.Id, levelId, start, end);
                SetDiameter(pipe, diameter);
                doc.Regenerate();

                ConnectPipeEndTo(doc, pipe.Id, start, fittingId, connId);
                doc.Regenerate();

                LogUtils.Log($"  33mm 배관 생성 Id={pipe.Id} {FormatXyz(start)} -> {FormatXyz(end)}");
                return pipe.Id;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"33mm 배관 생성 실패. 피팅 Id={fittingId} 커넥터 Id={connId}");
                return null;
            }
        }

        // ===== 2) 배관 통합 =====

        /// <summary>
        /// 두 배관을 하나로 합친다. 긴 배관이 남고 짧은 배관이 지워진다.
        /// 남는 배관을 짧은 배관의 반대쪽 끝(긴 배관 축 위로 투영한 점)까지 늘리고,
        /// 짧은 배관 반대쪽에 붙어 있던 객체는 남는 배관에 다시 붙인다.
        /// </summary>
        /// <returns>남은(직선) 배관 Id. 합치지 못했으면 긴 배관 Id 를 그대로 돌려준다.</returns>
        private static ElementId TryMergePipes(Document doc, PipeEnd endA, PipeEnd endB)
        {
            var pipeA = doc.GetElement(endA.PipeId) as Pipe;
            var pipeB = doc.GetElement(endB.PipeId) as Pipe;
            if (pipeA == null) return endB.PipeId;
            if (pipeB == null) return endA.PipeId;

            Line lineA = PipeGeometryUtils.GetPipeLine(pipeA);
            Line lineB = PipeGeometryUtils.GetPipeLine(pipeB);

            // 긴 배관이 남는다
            double lenA = (lineA != null) ? lineA.Length : 0.0;
            double lenB = (lineB != null) ? lineB.Length : 0.0;
            bool keepA = lenA >= lenB;

            PipeEnd keep = keepA ? endA : endB;
            PipeEnd remove = keepA ? endB : endA;
            Line keepLine = keepA ? lineA : lineB;
            Line removeLine = keepA ? lineB : lineA;

            if (keepLine == null || removeLine == null)
            {
                LogUtils.Log("  배관이 직선이 아니라 합치지 못했습니다. (통합 단계 건너뜀)");
                return keep.PipeId;
            }

            if (!PipeGeometryUtils.AreParallel(keepLine, removeLine))
            {
                double offDegree = PipeGeometryUtils.AngleBetween(keepLine, removeLine) * 180.0 / Math.PI;
                LogUtils.Log($"  두 배관이 평행하지 않아(약 {offDegree:F1}도) 합치지 못했습니다. (통합 단계 건너뜀)");
                return keep.PipeId;
            }

            try
            {
                // 짧은 배관의 반대쪽(먼 쪽) 끝점과, 그 끝을 긴 배관 축 위로 투영한 점
                XYZ rStart = removeLine.GetEndPoint(0);
                XYZ rEnd = removeLine.GetEndPoint(1);
                XYZ removeFar = (rStart.DistanceTo(remove.Origin) >= rEnd.DistanceTo(remove.Origin)) ? rStart : rEnd;
                XYZ targetPoint = PipeGeometryUtils.ProjectPointOnLine(keepLine, removeFar);

                // 짧은 배관 먼 쪽 끝에 붙어 있던 객체들을 기억해 둔다.
                var attachedIds = new List<ElementId>();
                Element removePipe = doc.GetElement(remove.PipeId);
                Connector removeFarConn = ElementUtils.FindNearestEndConnector(removePipe, removeFar);

                if (removeFarConn != null && removeFarConn.IsConnected)
                {
                    foreach (Connector other in removeFarConn.AllRefs)
                    {
                        if (other.ConnectorType != ConnectorType.End) continue;   // 논리적(System) 참조는 제외
                        if (other.Owner == null || other.Owner.Id == remove.PipeId) continue;
                        if (!attachedIds.Contains(other.Owner.Id)) attachedIds.Add(other.Owner.Id);
                    }
                }

                LogUtils.Log($"  배관 통합: 남김 Id={keep.PipeId}(길이 {Math.Max(lenA, lenB):F3}ft) 삭제 Id={remove.PipeId} " +
                    $"끝 {FormatXyz(keep.Origin)} -> {FormatXyz(targetPoint)} / 다시 붙일 객체 {attachedIds.Count}개");

                doc.Delete(remove.PipeId);
                doc.Regenerate();

                if (!PipeGeometryUtils.StretchPipeEndTo(doc, keep.PipeId, keep.Origin, targetPoint))
                    LogUtils.Log("  남는 배관을 늘리지 못했습니다. (너무 짧아지거나 직선이 아님)");
                doc.Regenerate();

                // 짧은 배관 먼 쪽에 붙어 있던 객체를 남는 배관의 새 끝에 다시 붙인다.
                foreach (ElementId attachedId in attachedIds)
                {
                    Element attached = doc.GetElement(attachedId);
                    var keepPipe = doc.GetElement(keep.PipeId) as Pipe;
                    if (attached == null || keepPipe == null) continue;

                    try
                    {
                        bool ok = ObjectConnectHelper.Connect(doc, keepPipe, attached);
                        LogUtils.Log($"  짧은 배관에 붙어 있던 객체(Id={attachedId}) 다시 연결: {ok}");
                    }
                    catch (Exception ex)
                    {
                        LogUtils.LogError(ex, $"짧은 배관에 붙어 있던 객체(Id={attachedId}) 다시 연결 실패.");
                    }
                }

                doc.Regenerate();
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "배관 통합 실패. (통합 단계 건너뜀)");
            }

            return keep.PipeId;
        }

        // ===== 3) 캡 붙이기 =====

        /// <summary>
        /// 캡(1커넥터 피팅)을 두 33mm 배관 중 캡에 가까운 쪽의 열린 끝에 이동·회전시켜 붙인다.
        /// </summary>
        /// <returns>캡을 붙인 33mm 배관 Id. 실패하면 null.</returns>
        private static ElementId TryAttachCap(Document doc, ElementId capId, ElementId branchAId, ElementId branchBId)
        {
            try
            {
                Element cap = doc.GetElement(capId);
                List<Connector> capConns = (cap != null) ? ElementUtils.GetOpenEndConnectors(cap) : null;
                if (capConns == null || capConns.Count != 1)
                {
                    LogUtils.Log("  캡의 열린 커넥터를 찾지 못해 캡 단계를 건너뜁니다.");
                    return null;
                }

                XYZ capOrigin = capConns[0].Origin;

                // 두 33mm 배관의 열린 끝 중 캡에 가까운 쪽
                Connector bestConn = null;
                ElementId bestBranchId = null;
                double bestDist = double.MaxValue;

                foreach (ElementId branchId in new[] { branchAId, branchBId })
                {
                    Element branch = doc.GetElement(branchId);
                    if (branch == null) continue;

                    foreach (Connector c in ElementUtils.GetOpenEndConnectors(branch))
                    {
                        double dist = c.Origin.DistanceTo(capOrigin);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestConn = c;
                            bestBranchId = branchId;
                        }
                    }
                }

                if (bestConn == null)
                {
                    LogUtils.Log("  33mm 배관에 열린 끝이 없어 캡 단계를 건너뜁니다.");
                    return null;
                }

                ElementId targetBranchId = bestBranchId;
                int branchConnId = bestConn.Id;
                int capConnId = capConns[0].Id;

                // 캡의 규격(ND1)을 붙을 배관의 ND 와 같게 맞춘다.
                // 규격이 바뀌면 캡의 형상과 커넥터 위치가 바뀌므로, 붙이기 "전에" 맞추고
                // 커넥터를 다시 찾아서 붙여야 정확한 자리에 놓인다.
                SetCapSizeToPipe(doc, capId, targetBranchId);
                doc.Regenerate();

                Connector branchConn = ElementUtils.ResolveConnector(doc, targetBranchId, branchConnId);
                Connector capConn = ElementUtils.ResolveConnector(doc, capId, capConnId);
                if (branchConn == null || capConn == null)
                {
                    LogUtils.Log("  규격을 맞춘 뒤 커넥터를 다시 찾지 못해 캡 단계를 건너뜁니다.");
                    return null;
                }

                // 캡이 움직여서 배관 끝에 붙는다.
                ConnectorHelper.AlignAndConnect(doc, branchConn, capConn, capId);
                doc.Regenerate();

                LogUtils.Log($"  캡(Id={capId})을 33mm 배관(Id={targetBranchId}) 끝에 붙였습니다.");
                return targetBranchId;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"캡(Id={capId}) 붙이기 실패. (캡 단계 건너뜀)");
                return null;
            }
        }

        /// <summary>
        /// 캡의 규격 파라미터(ND1)를 붙을 배관의 Nominal Diameter 와 같게 맞춘다.
        ///
        /// 패밀리 인스턴스의 커넥터 크기는 API 로 직접 바꿀 수 없고, 커넥터 크기를 정하는
        /// 패밀리 파라미터(여기서는 "ND1")를 바꿔야 형상과 커넥터가 함께 바뀐다.
        /// ND1 이 인스턴스 파라미터가 아니거나(타입 파라미터), 길이(Double) 형식이 아니면
        /// 값을 바꾸지 않고 로그만 남긴다. (타입 파라미터를 바꾸면 같은 타입의 다른 캡까지 바뀌기 때문)
        /// </summary>
        private static void SetCapSizeToPipe(Document doc, ElementId capId, ElementId branchPipeId)
        {
            Element cap = doc.GetElement(capId);
            var branch = doc.GetElement(branchPipeId) as Pipe;
            if (cap == null || branch == null) return;

            double pipeNd = branch.Diameter;   // 배관의 Nominal Diameter (feet)
            double pipeNdMm = UnitUtils.ConvertFromInternalUnits(pipeNd, UnitTypeId.Millimeters);

            Parameter ndParam = cap.LookupParameter(CapSizeParamName);
            if (ndParam == null)
            {
                bool isTypeParam = cap is FamilyInstance fi && fi.Symbol != null
                    && fi.Symbol.LookupParameter(CapSizeParamName) != null;

                LogUtils.Log(isTypeParam
                    ? $"  캡(Id={capId})의 '{CapSizeParamName}' 은 타입 파라미터라 값을 바꾸지 않습니다. (인스턴스 파라미터여야 함)"
                    : $"  캡(Id={capId})에 '{CapSizeParamName}' 파라미터가 없어 규격을 맞추지 않습니다.");
                return;
            }

            if (ndParam.IsReadOnly || ndParam.StorageType != StorageType.Double)
            {
                LogUtils.Log($"  캡(Id={capId})의 '{CapSizeParamName}' 이 읽기전용이거나 길이 형식이 아니라({ndParam.StorageType}) 규격을 맞추지 않습니다.");
                return;
            }

            if (Math.Abs(ndParam.AsDouble() - pipeNd) < PositionTolerance)
            {
                LogUtils.Log($"  캡(Id={capId}) 규격이 이미 {pipeNdMm:F0}mm 라 그대로 둡니다.");
                return;
            }

            ndParam.Set(pipeNd);
            LogUtils.Log($"  캡(Id={capId}) '{CapSizeParamName}' 을 {pipeNdMm:F0}mm 로 맞췄습니다.");
        }

        /// <summary>
        /// 캡이 없을 때, 두 33mm 배관 중 열린 끝이 직선배관 중심선에 더 가까운 쪽을 고른다.
        /// </summary>
        private static ElementId PickBranchCloserToPipe(Document doc, ElementId basePipeId, ElementId branchAId, ElementId branchBId)
        {
            var basePipe = doc.GetElement(basePipeId) as Pipe;
            Line baseLine = (basePipe != null) ? PipeGeometryUtils.GetPipeLine(basePipe) : null;
            if (baseLine == null) return null;

            ElementId best = null;
            double bestDist = double.MaxValue;

            foreach (ElementId branchId in new[] { branchAId, branchBId })
            {
                Element branch = doc.GetElement(branchId);
                if (branch == null) continue;

                foreach (Connector c in ElementUtils.GetOpenEndConnectors(branch))
                {
                    double dist = baseLine.Distance(c.Origin);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = branchId;
                    }
                }
            }

            return best;
        }

        // ===== 4) 덩어리 회전 =====

        /// <summary>
        /// 피팅 + 33mm 배관 2개 + 캡을 한 덩어리로 돌려서, 탭을 넣을 쪽 커넥터가 직선배관을 향하게 한다.
        ///
        /// 피팅 중심(두 커넥터의 가운데)에서 직선배관 중심선에 내린 수선 방향 d 를 구하고,
        /// "피팅 중심 → 탭 쪽 커넥터" 방향 u 가 d 와 나란해지도록 (u × d) 축으로 필요한 각도만큼 돌린다.
        /// 세로로 서 있는 피팅이면 이 각도가 곧 90도다. 이미 나란하면 돌리지 않는다.
        /// 여러 객체를 함께 돌리므로 서로의 연결 상태는 그대로 유지된다.
        /// </summary>
        private static bool TryRotateGroupTowardPipe(Document doc, ElementId fittingId, ElementId nearBranchId,
            ElementId basePipeId, List<ElementId> groupIds)
        {
            try
            {
                Element fitting = doc.GetElement(fittingId);
                List<Connector> conns = (fitting != null) ? ElementUtils.GetEndConnectors(fitting) : null;
                if (conns == null || conns.Count != 2) return false;

                var basePipe = doc.GetElement(basePipeId) as Pipe;
                Line baseLine = (basePipe != null) ? PipeGeometryUtils.GetPipeLine(basePipe) : null;
                if (baseLine == null)
                {
                    LogUtils.Log("  직선배관이 직선이 아니라 회전 단계를 건너뜁니다.");
                    return false;
                }

                XYZ fittingCenter = (conns[0].Origin + conns[1].Origin) * 0.5;

                // 탭 쪽 커넥터 = 탭 쪽 33mm 배관과 연결된 커넥터
                Connector nearConn = FindConnectorConnectedTo(conns, nearBranchId) ?? conns[0];

                XYZ toward = nearConn.Origin - fittingCenter;
                if (toward.IsZeroLength()) return false;
                toward = toward.Normalize();

                XYZ foot = PipeGeometryUtils.ProjectPointOnLine(baseLine, fittingCenter);
                XYZ toPipe = foot - fittingCenter;
                if (toPipe.GetLength() < PositionTolerance)
                {
                    LogUtils.Log("  피팅 중심이 직선배관 축 위에 있어 향할 방향을 정할 수 없어 회전 단계를 건너뜁니다.");
                    return false;
                }
                toPipe = toPipe.Normalize();

                double angle = toward.AngleTo(toPipe);
                if (angle < AlreadyFacingTolerance)
                {
                    LogUtils.Log("  피팅이 이미 직선배관을 향해 있어 돌리지 않습니다.");
                    return true;
                }

                XYZ rotationAxis = toward.CrossProduct(toPipe);
                if (rotationAxis.IsZeroLength())
                {
                    // 정확히 반대(180도)를 보고 있으면 외적이 0 → 직선배관 축을 회전축으로 쓴다
                    rotationAxis = baseLine.Direction;
                }
                rotationAxis = rotationAxis.Normalize();

                // 지워진 객체가 섞여 있지 않도록 걸러서 한 번에 돌린다.
                var validIds = groupIds.Where(id => doc.GetElement(id) != null).ToList();
                ElementTransformUtils.RotateElements(doc, validIds, Line.CreateUnbound(fittingCenter, rotationAxis), angle);
                doc.Regenerate();

                LogUtils.Log($"  피팅 덩어리({validIds.Count}개)를 {angle * 180.0 / Math.PI:F1}도 돌려 직선배관을 향하게 했습니다.");
                return true;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "피팅 회전 실패. (회전 단계 건너뜀)");
                return false;
            }
        }

        /// <summary>커넥터 목록 중 지정한 객체와 연결돼 있는 것. 없으면 null.</summary>
        private static Connector FindConnectorConnectedTo(List<Connector> conns, ElementId otherId)
        {
            foreach (Connector c in conns)
            {
                foreach (Connector other in c.AllRefs)
                {
                    if (other.ConnectorType != ConnectorType.End) continue;
                    if (other.Owner != null && other.Owner.Id == otherId) return c;
                }
            }

            return null;
        }

        // ===== 5) 탭 =====

        /// <summary>
        /// 탭 쪽 33mm 배관의 열린 끝을 직선배관 중심선까지 늘리고, 탭(Takeoff) 으로 붙인다.
        /// 배관 타입의 Routing Preferences 에 등록된 Tap 패밀리를 쓴다.
        /// </summary>
        private static void TryTapIntoPipe(Document doc, ElementId nearBranchId, ElementId fittingId, ElementId basePipeId)
        {
            try
            {
                var branch = doc.GetElement(nearBranchId) as Pipe;
                var trunk = doc.GetElement(basePipeId) as Pipe;
                Line trunkLine = (trunk != null) ? PipeGeometryUtils.GetPipeLine(trunk) : null;
                if (branch == null || trunkLine == null)
                {
                    LogUtils.Log("  탭 단계: 33mm 배관 / 직선배관을 찾지 못해 건너뜁니다.");
                    return;
                }

                List<Connector> openConns = ElementUtils.GetOpenEndConnectors(branch);
                if (openConns.Count != 1)
                {
                    LogUtils.Log("  탭 단계: 33mm 배관의 열린 끝이 1개가 아니라 건너뜁니다.");
                    return;
                }

                XYZ openEnd = openConns[0].Origin;
                XYZ foot = PipeGeometryUtils.ProjectPointOnLine(trunkLine, openEnd);

                // 열린 끝을 직선배관 중심선까지 늘린다. (피팅 쪽 끝은 그대로)
                if (!PipeGeometryUtils.StretchPipeEndTo(doc, nearBranchId, openEnd, foot))
                {
                    LogUtils.Log("  탭 단계: 33mm 배관을 직선배관까지 늘리지 못해 건너뜁니다.");
                    return;
                }
                doc.Regenerate();

                Connector tapConn = ElementUtils.FindNearestEndConnector(doc.GetElement(nearBranchId), foot);
                trunk = doc.GetElement(basePipeId) as Pipe;
                if (tapConn == null || trunk == null)
                {
                    LogUtils.Log("  탭 단계: 탭을 넣을 커넥터를 찾지 못해 건너뜁니다.");
                    return;
                }

                FamilyInstance tap = doc.Create.NewTakeoffFitting(tapConn, trunk);
                doc.Regenerate();

                LogUtils.Log($"  탭 삽입 성공 Id={tap.Id} (33mm 배관 Id={nearBranchId} → 직선배관 Id={basePipeId})");
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "탭(Takeoff) 삽입 실패. 배관 타입의 Routing Preferences 에 Tap 패밀리가 있는지 확인하세요.");
            }
        }

        // ===== 공통 =====

        /// <summary>새 배관의 커넥터 중 <paramref name="atPoint"/> 위치의 것을 지정한 객체의 커넥터에 연결한다.</summary>
        private static void ConnectPipeEndTo(Document doc, ElementId pipeId, XYZ atPoint, ElementId ownerId, int ownerConnId)
        {
            Element pipe = doc.GetElement(pipeId);
            Connector pipeConn = (pipe != null) ? ElementUtils.FindNearestEndConnector(pipe, atPoint) : null;
            Connector ownerConn = ElementUtils.ResolveConnector(doc, ownerId, ownerConnId);

            if (pipeConn == null || ownerConn == null) return;

            // 생성 위치가 커넥터와 딱 맞아도 자동으로 연결되지 않는 경우가 있어 명시적으로 연결한다.
            if (!pipeConn.IsConnectedTo(ownerConn))
                pipeConn.ConnectTo(ownerConn);
        }

        /// <summary>배관 지름을 맞춘다. (feet)</summary>
        private static void SetDiameter(Pipe pipe, double diameter)
        {
            Parameter diaParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
                diaParam.Set(diameter);
        }

        /// <summary>객체의 System Type Id. 없으면 InvalidElementId.</summary>
        private static ElementId GetSystemTypeId(Element e)
        {
            if (e == null) return ElementId.InvalidElementId;

            Parameter p = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId id = (p != null) ? p.AsElementId() : null;
            return (id != null) ? id : ElementId.InvalidElementId;
        }

        /// <summary>배관의 레벨 Id. 배관이 없거나 레벨이 없으면 지정한 점에서 가장 가까운 레벨.</summary>
        private static ElementId GetLevelId(Document doc, Pipe pipe, XYZ nearPoint)
        {
            ElementId levelId = ElementId.InvalidElementId;

            if (pipe != null)
            {
                Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null) levelId = p.AsElementId();
            }

            if (levelId == null || levelId == ElementId.InvalidElementId)
                levelId = ElementUtils.FindNearestLevelId(doc, nearPoint);

            return levelId;
        }

        private static string FormatXyz(XYZ p)
        {
            return p == null ? "null" : $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }
    }
}
