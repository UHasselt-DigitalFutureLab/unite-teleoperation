using System;
using Unite.Core;

namespace Unite.Kernel
{
    public interface IPackageSource
    {
        event Action<Package> PackageProduced;
    }

    public interface IPackageConsumer
    {
        void ReceivePackage(Package package);
    }
}
