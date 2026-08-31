using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 평행한 두 배관 사이에 "직각 배관"(= 두 중심선의 공통수선)을 놓기 위한 기하 계산 모음.
    ///
    /// [기하 원리]
    /// 평행한 두 배관의 중심선 L1 / L2 를 무한 직선으로 보면,
    /// 두 직선 모두와 직각으로 만나는 선분(= 공통수선)의 <b>방향과 길이</b>는 하나로 정해진다.
    ///   - 방향 : 배관 축 u 에 수직인 방향
    ///   - 길이 : 두 중심선 사이의 거리
    /// 다만 <b>축 방향 어디에 놓을지</b>는 정해지지 않는다(무수히 많다).
    /// 그래서 "축 방향 위치"(= station)를 하나 정해 주면 공통수선이 확정된다.
    ///
    /// 이 파일은 그 계산만 담당하고, station 을 어디로 정할지는
    /// <see cref="RightAngleConnectHelper"/> 가 결정한다.
    ///
    /// [평행 판정 허용오차]
    /// "대각 배관 생성기"(<see cref="DiagonalPipeHelper"/>)와 같은 약 1도를 쓴다.
    /// 같은 종류의 모델을 같은 기준으로 받아들이기 위해서다.
    /// 두 배관이 정확히 평행하지 않아도, 직각 배관은 <b>첫 배관 축에 정확히 수직</b>으로 만들고
    /// 반대쪽 끝점은 둘째 배관 중심선 위에 정확히 올려 놓으므로 연결 자체는 문제가 없다.
    /// (둘째 배관 쪽 엘보만 그 어긋난 각도만큼 90도에서 벗어난다)
    /// </summary>
    public static class PipeGeometryUtils
    {
        // 평행 판정 각도 허용오차(라디안). 약 1도. (대각 배관 생성기와 같은 값)
        private const double ParallelAngleTolerance = 0.0175;

        // 벡터 계산에서 0 으로 볼 값
        private const double Epsilon = 1e-9;

        /// <summary>
        /// 배관 하나의 중심선(Line)을 반환. 직선이 아니면(곡선 배관) null.
        /// </summary>
        public static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve lc = (pipe != null) ? pipe.Location as LocationCurve : null;
            return lc != null ? lc.Curve as Line : null;
        }

        /// <summary>
        /// 두 배관이 평행한지 검사. (같은 방향이든 반대 방향이든 평행이면 true)
        /// </summary>
        public static bool AreParallel(Line line1, Line line2)
        {
            double angle = AngleBetween(line1, line2);
            return angle < ParallelAngleTolerance;
        }

        /// <summary>
        /// 두 중심선이 이루는 각(라디안). 반대 방향도 평행으로 보기 위해 0 ~ 90도 범위로 접는다.
        /// 안내 문구에 "몇 도 어긋났는지" 를 보여주는 데도 쓴다.
        /// </summary>
        public static double AngleBetween(Line line1, Line line2)
        {
            double angle = line1.Direction.AngleTo(line2.Direction); // 0 ~ PI
            return (angle > Math.PI * 0.5) ? Math.PI - angle : angle;
        }

        /// <summary>
        /// 점을 축 방향으로 재었을 때의 위치(= station). 축 위 좌표 하나로 생각하면 된다.
        /// </summary>
        public static double GetStation(XYZ point, XYZ axis)
        {
            return point.DotProduct(axis);
        }

        /// <summary>
        /// 중심선 위에서 "축 방향 위치가 station 인 점" 을 구한다.
        ///
        /// 반드시 그 중심선 <b>위에</b> 있는 점을 돌려주므로, 이 점을 배관 끝점으로 쓰면
        /// 원래 배관을 그대로 연장/축소한 것이 된다.
        /// 중심선이 축과 수직에 가까우면(= 평행이 아니면) 구할 수 없으므로 false.
        /// </summary>
        public static bool TryGetPointAtStation(Line line, XYZ axis, double station, out XYZ point)
        {
            point = null;

            XYZ origin = line.GetEndPoint(0);
            XYZ direction = line.Direction.Normalize();

            // (origin + t * direction) 을 축으로 재면 station 이 되도록 t 를 구한다.
            double denom = direction.DotProduct(axis);
            if (Math.Abs(denom) < Epsilon) return false;   // 축과 수직 → 구할 수 없음

            double t = (station - origin.DotProduct(axis)) / denom;

            point = origin + t * direction;
            return true;
        }

        /// <summary>
        /// 두 중심선의 끝점 4개 조합 중 서로 가장 가까운 한 쌍을 찾는다.
        /// = 두 배관에서 "서로 마주보는 쪽 커넥터" 의 위치.
        /// </summary>
        public static void FindNearestEndPointPair(Line line1, Line line2, out XYZ near1, out XYZ near2)
        {
            near1 = line1.GetEndPoint(0);
            near2 = line2.GetEndPoint(0);
            double bestDist = double.MaxValue;

            for (int i = 0; i <= 1; i++)
            {
                for (int j = 0; j <= 1; j++)
                {
                    XYZ candidate1 = line1.GetEndPoint(i);
                    XYZ candidate2 = line2.GetEndPoint(j);

                    double dist = candidate1.DistanceTo(candidate2);
                    if (dist >= bestDist) continue;

                    bestDist = dist;
                    near1 = candidate1;
                    near2 = candidate2;
                }
            }
        }
    }
}
