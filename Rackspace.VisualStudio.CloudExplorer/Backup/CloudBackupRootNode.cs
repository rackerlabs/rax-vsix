﻿namespace Rackspace.VisualStudio.CloudExplorer.Backup
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.VSDesigner.ServerExplorer;
    using net.openstack.Core.Domain;

    public class CloudBackupRootNode : CloudProductRootNode
    {
        private readonly CloudIdentity _identity;

        private Node[] _children;

        public CloudBackupRootNode(ServiceCatalog serviceCatalog, CloudIdentity identity)
            : base(serviceCatalog)
        {
            _identity = identity;
        }

        protected override Task<Node[]> CreateChildrenAsync(CancellationToken cancellationToken)
        {
            if (_children == null)
            {
                List<Node> nodes = new List<Node>();
                foreach (Endpoint endpoint in ServiceCatalog.Endpoints)
                    nodes.Add(new CloudBackupEndpointNode(_identity, ServiceCatalog, endpoint));

                _children = nodes.ToArray();
            }

            return Task.FromResult(_children);
        }

        public override Image Icon
        {
            get
            {
                return ServerExplorerIcons.CloudBackup;
            }
        }

        protected override string DisplayText
        {
            get
            {
                return "Backup";
            }
        }
    }
}
