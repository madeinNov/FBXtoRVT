using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 커넥터를 "객체 Id + 커넥터 Id" 로 기억해 두는 참조.
    ///
    /// 삭제 / 이동 / 파라미터 변경이 일어나면 이미 손에 쥔 Connector 객체는 낡은 값을 갖게 된다.
    /// 그래서 커넥터 자체를 들고 다니지 않고 이 참조만 보관했다가,
    /// 실제로 쓰기 직전에 <see cref="ElementUtils.ResolveConnector"/> 로 다시 조회한다.
    ///
    /// 여러 기능(타공 슬리브 / ELBOW / 장비&amp;플랜지)이 같은 방식을 쓰므로 한 곳에 모아 두었다.
    /// </summary>
    public class ConnRef
    {
        /// <summary>커넥터를 가진 객체 Id</summary>
        public ElementId OwnerId;

        /// <summary>그 객체 안에서의 커넥터 Id</summary>
        public int ConnectorId;

        /// <summary>거리 비교용 좌표(수집한 시점 기준)</summary>
        public XYZ Origin;

        /// <summary>같은 커넥터를 두 번 쓰지 않도록 기록할 때 쓰는 키</summary>
        public string Key
        {
            get { return OwnerId.Value + ":" + ConnectorId; }
        }

        /// <summary>
        /// 지금 문서에서 실제 Connector 를 다시 조회한다. 못 찾으면 null.
        /// </summary>
        public Connector Resolve(Document doc)
        {
            return ElementUtils.ResolveConnector(doc, OwnerId, ConnectorId);
        }

        /// <summary>
        /// Connector 하나로부터 참조를 만든다.
        /// </summary>
        public static ConnRef From(ElementId ownerId, Connector c)
        {
            return new ConnRef
            {
                OwnerId = ownerId,
                ConnectorId = c.Id,
                Origin = c.Origin
            };
        }
    }
}
