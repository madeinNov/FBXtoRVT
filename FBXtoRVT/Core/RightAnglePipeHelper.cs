using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "직각 배관 생성기" 기능의 핵심 로직.
    ///
    /// [무엇을 하는 기능인가]
    /// 서로 직각이지만 연장해도 만나지 않는(= 꼬인 위치) 두 배관 사이에,
    /// 둘을 잇는 "사잇배관" 을 만들어 준다.
    /// 이 사잇배관이 있어야 유저가 Trim 으로 Elbow 를 2번 넣을 수 있다.
    ///
    /// [기하 원리]
    /// 두 배관의 중심선을 무한 직선으로 보면, 꼬인 위치의 두 직선에는
    /// 양쪽 모두와 직각으로 만나는 선분이 딱 하나 존재한다. (= 공통수선)
    ///   - 공통수선의 발 P1 : 첫 배관 직선 위의 점
    ///   - 공통수선의 발 P2 : 둘째 배관 직선 위의 점
    /// P1 ~ P2 를 잇는 배관이 곧 사잇배관이며, 양 끝점이 두 배관의 연장선 위에
    /// 정확히 놓이므로 Trim 이 가능하다.
    ///
    /// 이미 두 직선이 만나는 경우(공통수선 길이 ≈ 0)는 사잇배관이 필요 없으므로 건너뛴다.
    ///
    /// [선택 방식 2가지]
    /// 어느 쪽이든 직각 + 꼬인 위치 조건을 만족하는 조합 중,
    /// 서로 가장 가까운 쌍부터 자동으로 짝지어 나간다.
    ///
    ///  1) 2Click (<see cref="Run"/>)
    ///     1차/2차로 나눠서 선택한다. 짝은 1차 × 2차 조합에서만 만들어지고,
    ///     새 배관의 속성은 1차로 선택한 배관을 따라간다.
    ///     한쪽 선택이 배관 1개뿐이면 그 배관을 여러 번 재사용한다. (1 : N 매칭)
    ///
    ///  2) 1Click (<see cref="RunOneSelection"/>)
    ///     한 번에 모아서 선택한다. 선택한 배관들끼리 짝을 짓고(배관 하나는 한 번만 쓰임),
    ///     새 배관의 속성은 그 쌍에서 중심점 Z 가 더 높은 배관을 따라간다.
    ///
    /// 어느 쪽이든 새 배관의 배관 타입 / System Type / 지름 / 레벨은 "기준 배관" 을 따라간다.
    /// </summary>
    public static class RightAnglePipeHelper
    {
        // 직각 판정 허용오차. "정확히 직각인 배관만" 통과시킨다.
        //
        // 두 방향벡터의 내적이 0 이면 직각이다. 다만 이 값을 문자 그대로 0 으로 두면,
        // 정확히 직각인 배관도 부동소수점 계산 찌꺼기(1e-17 수준) 때문에 걸러져 버린다.
        // 그래서 "표현 오차만 허용"하는 아주 작은 값을 쓴다. 각도로는 약 0.00000006도.
        private const double PerpendicularDotTolerance = 1e-9;

        // 벡터 계산에서 0 으로 볼 값
        private const double Epsilon = 1e-9;

        // 두 배관의 높이(Z)가 같다고 볼 허용오차(feet). 약 0.0003mm.
        private const double SameHeightTolerance = 1e-6;

        /// <summary>
        /// 배관 하나의 중심선(Line)을 반환. 직선이 아니면 null.
        /// </summary>
        public static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve lc = (pipe != null) ? pipe.Location as LocationCurve : null;
            return lc != null ? lc.Curve as Line : null;
        }

        /// <summary>
        /// 배관 + 중심선을 함께 들고 다니기 위한 묶음.
        /// </summary>
        private class PipeAxis
        {
            public Pipe Pipe;
            public Line Line;
        }

        /// <summary>
        /// 사잇배관 하나를 만들기 위한 정보. (짝짓기 결과)
        /// </summary>
        private class PairCandidate
        {
            public PipeAxis Base;       // 기준 배관 (타입/지름/System Type/레벨 출처)
            public PipeAxis Target;     // 상대 배관
            public XYZ BaseFoot;        // 공통수선의 발 (기준 배관 직선 위)
            public XYZ TargetFoot;      // 공통수선의 발 (상대 배관 직선 위)
            public double Nearness;     // 두 배관(실제 선분) 사이 최단거리 = 짝짓기 우선순위
        }

        /// <summary>
        /// 실행 결과 요약. Commands 쪽에서 대화상자로 보여준다.
        /// </summary>
        public class RunResult
        {
            /// <summary>생성된 사잇배관 Id 목록</summary>
            public List<ElementId> CreatedPipeIds = new List<ElementId>();

            /// <summary>직선이 아니라 제외한 배관 수</summary>
            public int NotStraightCount;

            /// <summary>이미 직각으로 만나서 사잇배관이 필요 없는 배관 수</summary>
            public int AlreadyIntersectCount;

            /// <summary>직각인 상대 배관을 못 찾은 배관 수</summary>
            public int NoPerpendicularCount;

            /// <summary>직각 상대는 있었지만 그 배관이 다른 쌍에 먼저 배정된 배관 수</summary>
            public int PartnerTakenCount;

            /// <summary>Revit 배관 생성 단계에서 실패한 쌍의 수</summary>
            public int CreateFailedCount;

            public int CreatedCount
            {
                get { return CreatedPipeIds.Count; }
            }
        }

        /// <summary>
        /// [1Click] 한 번에 선택한 배관들끼리 짝지어 사잇배관을 만든다.
        /// (외부에서 Transaction 을 열고 호출)
        ///
        /// 2Click 과 다른 점은 두 가지다.
        ///  - 짝짓기 후보가 "선택한 배관들 사이의 모든 조합" 이고, 배관 하나는 한 번만 쓰인다.
        ///  - 기준 배관(속성 출처)은 그 쌍에서 중심점 Z 가 더 높은 배관이다.
        /// </summary>
        /// <param name="pipes">한 번에 선택한 배관들</param>
        public static RunResult RunOneSelection(Document doc, IList<Pipe> pipes)
        {
            var result = new RunResult();

            double minLength = doc.Application.ShortCurveTolerance;

            // 1) 직선 배관만 남긴다.
            List<PipeAxis> axes = ToAxes(pipes, result);
            if (axes.Count < 2) return result;

            // 2) 선택한 배관들끼리의 모든 조합 중 조건을 만족하는 것을 후보로 모은다.
            //    기준 배관은 각 쌍에서 Z 가 더 높은 쪽으로 정한다.
            var candidates = new List<PairCandidate>();

            for (int i = 0; i < axes.Count; i++)
            {
                for (int j = i + 1; j < axes.Count; j++)
                {
                    PairCandidate candidate = TryMakeCandidate(axes[i], axes[j], minLength, true);
                    if (candidate != null) candidates.Add(candidate);
                }
            }

            // 3) 가까운 쌍부터 배정. 이미 짝지어진 배관은 다시 쓰지 않는다.
            List<PairCandidate> matched = MatchPairsWithinOneGroup(candidates);

            // 4) 배정된 쌍마다 사잇배관 생성
            //    handled = 짝을 배정받은 배관. (생성에 실패했더라도 사유 집계에서는 제외)
            var handled = new HashSet<ElementId>();

            foreach (PairCandidate pair in matched)
            {
                handled.Add(pair.Base.Pipe.Id);
                handled.Add(pair.Target.Pipe.Id);

                Pipe newPipe = TryCreatePipe(doc, pair);

                if (newPipe == null)
                {
                    result.CreateFailedCount++;
                    continue;
                }

                result.CreatedPipeIds.Add(newPipe.Id);
            }

            // 5) 짝을 배정받지 못한 배관에 대해 이유를 집계한다.
            //    (후보를 같은 그룹 안에서 찾으므로 두 인자에 같은 목록을 넘긴다)
            CountSkipReasons(axes, axes, handled, minLength, result);

            return result;
        }

        /// <summary>
        /// [2Click] 1차 선택 배관들과 2차 선택 배관들을 짝지어 사잇배관을 만든다.
        /// (외부에서 Transaction 을 열고 호출)
        /// </summary>
        /// <param name="basePipes">1차 선택 배관들 (타입/지름/System Type 기준)</param>
        /// <param name="targetPipes">2차 선택 배관들</param>
        public static RunResult Run(Document doc, IList<Pipe> basePipes, IList<Pipe> targetPipes)
        {
            var result = new RunResult();

            // 배관을 만들 수 있는 최소 길이. 이보다 짧으면 Revit 이 배관을 만들지 못한다.
            double minLength = doc.Application.ShortCurveTolerance;

            // 1) 직선 배관만 남긴다. (곡선 배관은 중심선을 직선으로 다룰 수 없음)
            List<PipeAxis> baseAxes = ToAxes(basePipes, result);
            List<PipeAxis> targetAxes = ToAxes(targetPipes, result);

            if (baseAxes.Count == 0 || targetAxes.Count == 0)
                return result;

            // 2) 조건(직각 + 꼬인 위치)을 만족하는 모든 조합을 후보로 모은다.
            List<PairCandidate> candidates = CollectCandidates(baseAxes, targetAxes, minLength);

            // 3) 가까운 쌍부터 1:1 로 배정한다.
            //    단, 한쪽 선택이 배관 1개뿐이면 그 배관은 여러 번 재사용한다. (1 : N)
            bool reuseBase = (baseAxes.Count == 1);
            bool reuseTarget = (targetAxes.Count == 1);
            List<PairCandidate> matched = MatchPairs(candidates, reuseBase, reuseTarget);

            // 4) 배정된 쌍마다 사잇배관 생성
            //    handledBases = 짝을 배정받은 기준 배관. (생성에 실패했더라도 사유 집계에서는 제외)
            var handledBases = new HashSet<ElementId>();

            foreach (PairCandidate pair in matched)
            {
                handledBases.Add(pair.Base.Pipe.Id);

                Pipe newPipe = TryCreatePipe(doc, pair);

                if (newPipe == null)
                {
                    result.CreateFailedCount++;
                    continue;
                }

                result.CreatedPipeIds.Add(newPipe.Id);
            }

            // 5) 짝을 배정받지 못한 기준 배관에 대해 이유를 집계한다.
            CountSkipReasons(baseAxes, targetAxes, handledBases, minLength, result);

            return result;
        }

        // ===== 1) 직선 배관 추리기 =====

        /// <summary>
        /// 배관 목록에서 중심선이 직선인 것만 골라 PipeAxis 로 만든다.
        /// 중복 선택된 배관은 한 번만 담는다.
        /// </summary>
        private static List<PipeAxis> ToAxes(IList<Pipe> pipes, RunResult result)
        {
            var list = new List<PipeAxis>();
            var seen = new HashSet<ElementId>();

            if (pipes == null) return list;

            foreach (Pipe pipe in pipes)
            {
                if (pipe == null) continue;
                if (!seen.Add(pipe.Id)) continue;   // 같은 배관 중복 선택은 무시

                Line line = GetPipeLine(pipe);
                if (line == null)
                {
                    result.NotStraightCount++;      // 곡선 배관은 대상이 아님
                    continue;
                }

                list.Add(new PipeAxis { Pipe = pipe, Line = line });
            }

            return list;
        }

        // ===== 2) 후보 조합 모으기 =====

        /// <summary>
        /// 1차 배관 × 2차 배관 모든 조합 중, 사잇배관을 만들 수 있는 것만 후보로 모은다.
        /// (2Click 전용. 기준 배관은 항상 1차 선택 배관이다)
        /// </summary>
        private static List<PairCandidate> CollectCandidates(
            List<PipeAxis> baseAxes, List<PipeAxis> targetAxes, double minLength)
        {
            var candidates = new List<PairCandidate>();

            foreach (PipeAxis b in baseAxes)
            {
                foreach (PipeAxis t in targetAxes)
                {
                    PairCandidate candidate = TryMakeCandidate(b, t, minLength, false);
                    if (candidate != null) candidates.Add(candidate);
                }
            }

            return candidates;
        }

        /// <summary>
        /// 배관 두 개가 사잇배관을 만들 수 있는 조합인지 검사하고, 맞으면 후보를 만든다.
        /// 조건: (a) 두 중심선이 직각  (b) 연장해도 만나지 않음(공통수선 길이가 최소 길이 이상)
        /// </summary>
        /// <param name="chooseHigherAsBase">
        /// true 면 중심점 Z 가 더 높은 배관을 기준으로 삼는다. (1Click)
        /// false 면 첫 인자를 그대로 기준으로 삼는다. (2Click — 1차 선택 배관이 기준)
        /// </param>
        private static PairCandidate TryMakeCandidate(
            PipeAxis first, PipeAxis second, double minLength, bool chooseHigherAsBase)
        {
            if (first.Pipe.Id == second.Pipe.Id) return null;       // 같은 배관끼리는 제외

            if (!IsPerpendicular(first.Line, second.Line)) return null;

            // 기준 / 상대 배관을 정한다.
            PipeAxis baseAxis = first;
            PipeAxis targetAxis = second;

            if (chooseHigherAsBase && !ShouldBeBase(first, second))
            {
                baseAxis = second;
                targetAxis = first;
            }

            XYZ baseFoot, targetFoot;
            if (!TryGetCommonPerpendicular(baseAxis.Line, targetAxis.Line, out baseFoot, out targetFoot))
                return null;

            // 공통수선 길이 = 만들어질 사잇배관의 길이
            if (baseFoot.DistanceTo(targetFoot) < minLength) return null;   // 이미 만남 → 사잇배관 불필요

            return new PairCandidate
            {
                Base = baseAxis,
                Target = targetAxis,
                BaseFoot = baseFoot,
                TargetFoot = targetFoot,
                Nearness = SegmentDistance(first.Line, second.Line)
            };
        }

        /// <summary>
        /// 두 배관 중 어느 쪽을 기준(배관 타입 / 지름 / System Type / 레벨의 출처)으로 삼을지 판정.
        /// 중심점의 Z 가 더 높은 배관이 기준이다.
        /// Z 까지 같으면 어느 쪽이든 상관없지만, 실행할 때마다 결과가 달라지지 않도록
        /// Element Id 가 작은 쪽으로 고정한다.
        /// </summary>
        private static bool ShouldBeBase(PipeAxis candidate, PipeAxis other)
        {
            double z1 = CenterZ(candidate.Line);
            double z2 = CenterZ(other.Line);

            if (Math.Abs(z1 - z2) > SameHeightTolerance) return z1 > z2;

            return candidate.Pipe.Id.Value < other.Pipe.Id.Value;
        }

        /// <summary>배관 중심선의 중점 Z (= 그 배관의 높이).</summary>
        private static double CenterZ(Line line)
        {
            return (line.GetEndPoint(0).Z + line.GetEndPoint(1).Z) * 0.5;
        }

        // ===== 3) 짝짓기 =====

        /// <summary>
        /// 가까운 쌍부터 순서대로 배정한다.
        /// reuseBase / reuseTarget 이 false 면 그쪽 배관은 한 번만 쓰인다.(1:1)
        /// </summary>
        private static List<PairCandidate> MatchPairs(
            List<PairCandidate> candidates, bool reuseBase, bool reuseTarget)
        {
            // 가까운 순으로 정렬 → 가장 자연스러운 짝부터 확정
            candidates.Sort((x, y) => x.Nearness.CompareTo(y.Nearness));

            var usedBase = new HashSet<ElementId>();
            var usedTarget = new HashSet<ElementId>();
            var matched = new List<PairCandidate>();

            foreach (PairCandidate c in candidates)
            {
                if (!reuseBase && usedBase.Contains(c.Base.Pipe.Id)) continue;
                if (!reuseTarget && usedTarget.Contains(c.Target.Pipe.Id)) continue;

                matched.Add(c);
                usedBase.Add(c.Base.Pipe.Id);
                usedTarget.Add(c.Target.Pipe.Id);
            }

            return matched;
        }

        /// <summary>
        /// 한 그룹 안에서 짝짓기. (1Click 전용)
        /// 가까운 쌍부터 배정하고, 한 번 짝지어진 배관은 기준이든 상대든 다시 쓰지 않는다.
        /// </summary>
        private static List<PairCandidate> MatchPairsWithinOneGroup(List<PairCandidate> candidates)
        {
            // 가까운 순으로 정렬 → 가장 자연스러운 짝부터 확정
            candidates.Sort((x, y) => x.Nearness.CompareTo(y.Nearness));

            var used = new HashSet<ElementId>();
            var matched = new List<PairCandidate>();

            foreach (PairCandidate c in candidates)
            {
                if (used.Contains(c.Base.Pipe.Id)) continue;
                if (used.Contains(c.Target.Pipe.Id)) continue;

                matched.Add(c);
                used.Add(c.Base.Pipe.Id);
                used.Add(c.Target.Pipe.Id);
            }

            return matched;
        }

        // ===== 4) 배관 생성 =====

        /// <summary>
        /// 공통수선의 발 두 점을 잇는 사잇배관을 만든다.
        /// 타입 / System Type / 지름 / 레벨은 1차 선택 배관을 따라간다.
        /// 실패하면 null.
        /// </summary>
        private static Pipe TryCreatePipe(Document doc, PairCandidate pair)
        {
            Pipe basePipe = pair.Base.Pipe;

            try
            {
                ElementId pipeTypeId = basePipe.GetTypeId();

                Parameter systemParam = basePipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                ElementId systemTypeId = (systemParam != null)
                    ? systemParam.AsElementId()
                    : ElementId.InvalidElementId;

                Parameter levelParam = basePipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                ElementId levelId = (levelParam != null)
                    ? levelParam.AsElementId()
                    : ElementId.InvalidElementId;

                // 기준 배관에 레벨 정보가 없으면 생성 위치에서 가장 가까운 레벨을 쓴다.
                if (levelId == ElementId.InvalidElementId)
                    levelId = ElementUtils.FindNearestLevelId(doc, pair.BaseFoot);

                Pipe newPipe = Pipe.Create(
                    doc, systemTypeId, pipeTypeId, levelId, pair.BaseFoot, pair.TargetFoot);

                // 지름도 1차 선택 배관과 동일하게 맞춘다.
                Parameter diaParam = newPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diaParam != null && !diaParam.IsReadOnly)
                {
                    diaParam.Set(basePipe.Diameter);
                }

                return newPipe;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "사잇배관 생성 실패 (기준 배관 Id: " + basePipe.Id + ")");
                return null;
            }
        }

        // ===== 5) 건너뛴 이유 집계 =====

        /// <summary>
        /// 짝을 배정받지 못한 기준 배관마다 이유를 판정해 카운트한다.
        ///  - 직각인 상대가 아예 없었다
        ///  - 직각이긴 한데 이미 만나고 있어서 사잇배관이 필요 없다
        ///  - 조건은 맞았지만 그 상대가 다른 쌍에 먼저 배정됐다
        /// </summary>
        private static void CountSkipReasons(
            List<PipeAxis> baseAxes, List<PipeAxis> targetAxes,
            HashSet<ElementId> handledBases, double minLength, RunResult result)
        {
            foreach (PipeAxis b in baseAxes)
            {
                if (handledBases.Contains(b.Pipe.Id)) continue;   // 짝을 배정받았으면 통과

                bool hasPerpendicular = false;      // 직각인 상대가 있었나
                bool hasIntersecting = false;       // 그중 이미 만나는 상대가 있었나
                bool hasSkewPartner = false;        // 그중 사잇배관이 필요한 상대가 있었나

                foreach (PipeAxis t in targetAxes)
                {
                    if (b.Pipe.Id == t.Pipe.Id) continue;
                    if (!IsPerpendicular(b.Line, t.Line)) continue;

                    hasPerpendicular = true;

                    XYZ foot1, foot2;
                    if (!TryGetCommonPerpendicular(b.Line, t.Line, out foot1, out foot2)) continue;

                    if (foot1.DistanceTo(foot2) < minLength) hasIntersecting = true;
                    else hasSkewPartner = true;
                }

                if (!hasPerpendicular) result.NoPerpendicularCount++;
                else if (hasSkewPartner) result.PartnerTakenCount++;   // 상대를 다른 쌍에 빼앗김
                else if (hasIntersecting) result.AlreadyIntersectCount++;
                else result.NoPerpendicularCount++;
            }
        }

        // ===== 기하 계산 =====

        /// <summary>
        /// 두 중심선이 직각인지 검사.
        /// 단위벡터끼리의 내적 = cos(사잇각) 이므로, 이 값이 0 이면 정확히 직각이다.
        /// (오차는 부동소수점 표현 오차만 허용 — 사실상 0도 오차)
        /// </summary>
        public static bool IsPerpendicular(Line line1, Line line2)
        {
            double dot = line1.Direction.Normalize().DotProduct(line2.Direction.Normalize());
            return Math.Abs(dot) < PerpendicularDotTolerance;
        }

        /// <summary>
        /// 두 직선(무한 연장)의 공통수선의 발을 구한다.
        /// foot1 = 첫 직선 위의 점, foot2 = 둘째 직선 위의 점.
        /// 두 직선이 평행이면 공통수선이 하나로 정해지지 않으므로 false.
        /// </summary>
        public static bool TryGetCommonPerpendicular(Line line1, Line line2, out XYZ foot1, out XYZ foot2)
        {
            foot1 = null;
            foot2 = null;

            XYZ p0 = line1.GetEndPoint(0);
            XYZ q0 = line2.GetEndPoint(0);
            XYZ u = line1.Direction.Normalize();
            XYZ v = line2.Direction.Normalize();

            XYZ w0 = p0 - q0;

            double b = u.DotProduct(v);         // 두 방향의 사잇각 코사인
            double d = u.DotProduct(w0);
            double e = v.DotProduct(w0);

            double denom = 1.0 - b * b;         // u, v 가 단위벡터이므로 (u·u)(v·v) - (u·v)^2
            if (Math.Abs(denom) < Epsilon) return false;   // 평행 → 공통수선이 무수히 많음

            double s = (b * e - d) / denom;     // 첫 직선 위 매개변수
            double t = (e - b * d) / denom;     // 둘째 직선 위 매개변수

            foot1 = p0 + s * u;
            foot2 = q0 + t * v;
            return true;
        }

        /// <summary>
        /// 두 선분(무한 직선이 아니라 실제 배관 구간) 사이의 최단거리.
        /// 어느 배관끼리 짝지을지 정하는 "가까움" 기준으로 쓴다.
        /// </summary>
        private static double SegmentDistance(Line line1, Line line2)
        {
            XYZ p1 = line1.GetEndPoint(0);
            XYZ p2 = line2.GetEndPoint(0);
            XYZ d1 = line1.GetEndPoint(1) - p1;     // 선분 1의 방향 * 길이
            XYZ d2 = line2.GetEndPoint(1) - p2;     // 선분 2의 방향 * 길이
            XYZ r = p1 - p2;

            double a = d1.DotProduct(d1);
            double e = d2.DotProduct(d2);
            double f = d2.DotProduct(r);

            double s, t;

            if (a < Epsilon && e < Epsilon)
                return r.GetLength();               // 둘 다 사실상 점

            if (a < Epsilon)
            {
                s = 0.0;
                t = Clamp01(f / e);
            }
            else
            {
                double c = d1.DotProduct(r);

                if (e < Epsilon)
                {
                    t = 0.0;
                    s = Clamp01(-c / a);
                }
                else
                {
                    double b = d1.DotProduct(d2);
                    double denom = a * e - b * b;

                    s = (denom > Epsilon) ? Clamp01((b * f - c * e) / denom) : 0.0;

                    t = (b * s + f) / e;

                    // t 가 선분 밖으로 나가면 끝점으로 밀어 넣고 s 를 다시 계산
                    if (t < 0.0)
                    {
                        t = 0.0;
                        s = Clamp01(-c / a);
                    }
                    else if (t > 1.0)
                    {
                        t = 1.0;
                        s = Clamp01((b - c) / a);
                    }
                }
            }

            XYZ closest1 = p1 + s * d1;
            XYZ closest2 = p2 + t * d2;
            return closest1.DistanceTo(closest2);
        }

        /// <summary>값을 0~1 범위로 자른다.</summary>
        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
