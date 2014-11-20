﻿namespace Rackspace.VisualStudio.CloudExplorer.Files
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using Microsoft.VSDesigner.ServerExplorer;
    using net.openstack.Core.Domain;
    using net.openstack.Core.Exceptions.Response;
    using net.openstack.Providers.Rackspace;
    using Container = net.openstack.Core.Domain.Container;
    using Image = System.Drawing.Image;
    using LocalizableProperties = Microsoft.VisualStudio.Shell.LocalizableProperties;

    public class CloudFilesContainerNode : AsyncNode
    {
        private readonly CloudFilesProvider _provider;
        private readonly Container _container;
        private readonly ContainerCDN _containerCdn;

        public CloudFilesContainerNode(CloudFilesProvider provider, Container container, ContainerCDN containerCdn)
        {
            if (provider == null)
                throw new ArgumentNullException("provider");
            if (container == null)
                throw new ArgumentNullException("container");

            _provider = provider;
            _container = container;
            _containerCdn = containerCdn;
        }

        protected override async Task<Node[]> CreateChildrenAsync(CancellationToken cancellationToken)
        {
            int limit = 100;
            ContainerObject[] objects = await ListObjectsAsync(cancellationToken, limit);
            Node[] nodes = Array.ConvertAll(objects, i => CreateObjectNode(i));
            if (nodes.Length == limit)
            {
                Array.Resize(ref nodes, nodes.Length + 1);
                nodes[nodes.Length - 1] = new NotImplementedPlaceholderNode();
            }

            return nodes;
        }

        private CloudFilesObjectNode CreateObjectNode(ContainerObject containerObject)
        {
            return new CloudFilesObjectNode(_provider, _container, containerObject);
        }

        private async Task<ContainerObject[]> ListObjectsAsync(CancellationToken cancellationToken, int limit)
        {
            List<ContainerObject> objects = new List<ContainerObject>();
            objects.AddRange(await Task.Run(() => _provider.ListObjects(_container.Name, limit)));
            return objects.ToArray();
        }

        public override Image Icon
        {
            get
            {
                return ServerExplorerIcons.CloudFilesContainer;
            }
        }

        protected override string DisplayText
        {
            get
            {
                return _container.Name;
            }
        }

        public override bool CanDeleteNode()
        {
            return true;
        }

        protected override DialogResult ConfirmUserDeletingNodeImpl()
        {
            string message = string.Format("Are you sure you want to delete the container \"{0}\" (and all its contents)?", _container.Name);
            INodeSite nodeSite = GetNodeSite();
            if (nodeSite == null)
                return DialogResult.Cancel;

            return nodeSite.ShowMessageBox(message, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        protected override async Task<bool> DeleteNodeAsync(CancellationToken cancellationToken, System.IProgress<int> progress)
        {
            // use a default instance of Progress<int> so we don't have to use null checks throughout the method
            if (progress == null)
                progress = new Progress<int>();

            // get the current container object count as a second estimate of the number of objects we're deleting.
            Dictionary<string, string> headers = _provider.GetContainerHeader(_container.Name);
            string containerObjectCountString = null;
            int containerObjectCount = 0;
            if (headers != null && headers.TryGetValue(CloudFilesProvider.ContainerObjectCount, out containerObjectCountString))
            {
                if (!int.TryParse(containerObjectCountString, out containerObjectCount))
                    containerObjectCount = _container.Count;
            }

            int deletedEstimate = 0;
            int progressEstimate = 0;
            progress.Report(0);

            while (true)
            {
                IEnumerable<ContainerObject> objects = await Task.Run(() => _provider.ListObjects(_container.Name)).ConfigureAwait(false);
                if (objects == null)
                    continue;

                ContainerObject[] objectsArray = objects.ToArray();
                if (objectsArray.Length == 0)
                    break;

                int totalCount = objectsArray.Length;
                if (container != null)
                    totalCount = Math.Max(totalCount, containerObjectCount);

                int count = 0;
                Action<Task> continuation =
                    task =>
                    {
                        // ignore ItemNotFoundException
                        if (task.Exception != null && !(task.Exception.InnerException is ItemNotFoundException))
                            throw task.Exception;

                        Interlocked.Increment(ref deletedEstimate);
                        count++;
                        int updatedProgress = (int)Math.Round((100.0 * deletedEstimate) / totalCount, 0, MidpointRounding.AwayFromZero);
                        updatedProgress = Math.Max(0, Math.Min(100, updatedProgress));
                        if (updatedProgress != progressEstimate)
                        {
                            progressEstimate = updatedProgress;
                            progress.Report(updatedProgress);
                        }
                    };

                Task[] deleteObjectTasks = Array.ConvertAll(objectsArray,
                    obj => Task.Run(() => _provider.DeleteObject(_container.Name, obj.Name))
                        .ContinueWith(continuation));
                await Task.WhenAll(deleteObjectTasks).ConfigureAwait(false);
            }

            await Task.Run(() => _provider.DeleteContainer(_container.Name)).ConfigureAwait(false);
            return true;
        }

        public override object GetBrowseComponent()
        {
            return new ContainerProperties(_provider, _container, _containerCdn);
        }

        public class ContainerProperties : LocalizableProperties, ICustomTypeDescriptor
        {
            private readonly CloudFilesProvider _provider;
            private readonly Container _container;
            private readonly ContainerCDN _containerCdn;

            public ContainerProperties(CloudFilesProvider provider, Container container, ContainerCDN containerCdn)
            {
                if (provider == null)
                    throw new ArgumentNullException("provider");
                if (container == null)
                    throw new ArgumentNullException("container");

                _provider = provider;
                _container = container;
                _containerCdn = containerCdn;
            }

            [DisplayName("Name")]
            [Category(PropertyCategories.Identity)]
            public string Name
            {
                get
                {
                    return _container.Name;
                }
            }

            [DisplayName("Region")]
            [Category(PropertyCategories.Identity)]
            public string Region
            {
                get
                {
                    return _provider.DefaultRegion;
                }
            }

            [DisplayName("Size")]
            public long Size
            {
                get
                {
                    return _container.Bytes;
                }
            }

            [DisplayName("Object Count")]
            public long Count
            {
                get
                {
                    return _container.Count;
                }
            }

            public override string GetClassName()
            {
                return "Container Properties";
            }

            string ICustomTypeDescriptor.GetComponentName()
            {
                return "Cloud Files";
            }
        }
    }
}
