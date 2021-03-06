﻿using System;
using System.Collections.Generic;
using System.Xml;
using log4net;

namespace winsw.Extensions
{
    public class WinSWExtensionManager
    {
        public Dictionary<string, IWinSWExtension> Extensions { get; private set; }

        public ServiceDescriptor ServiceDescriptor { get; private set; }

        private static readonly ILog Log = LogManager.GetLogger(typeof(WinSWExtensionManager));

        public WinSWExtensionManager(ServiceDescriptor serviceDescriptor)
        {
            ServiceDescriptor = serviceDescriptor;
            Extensions = new Dictionary<string, IWinSWExtension>();
        }

        /// <summary>
        /// Notifies all extensions that the wrapper is being started.
        /// They are supposed to run the initialization logic.
        /// If any extensions fails, WinSW startup should be interrupted.
        /// </summary>
        /// <exception cref="Exception">Start failure</exception>
        public void FireOnWrapperStarted()
        {
            foreach (var ext in Extensions)
            {
                try
                {
                    ext.Value.OnWrapperStarted();
                }
                catch (ExtensionException ex)
                {
                    Log.Fatal("onWrapperStarted() handler failed for " + ext.Value.DisplayName, ex);
                    throw ex; // Propagate error to stop the startup
                }
            }
        }

        /// <summary>
        /// Notifies all extensions that the wrapper is being stopped.
        /// If an error happens, further extensions will be tried
        /// </summary>
        public void FireBeforeWrapperStopped()
        {
            foreach (var ext in Extensions)
            {
                try
                {
                    ext.Value.BeforeWrapperStopped();
                }
                catch (ExtensionException ex)
                {
                    Log.Error("beforeWrapperStopped() handler failed for " + ext.Value.DisplayName, ex);
                }
            }
        }

        /// <summary>
        /// Handler, which is being invoked once the child process is started.
        /// </summary>
        /// <param name="process">Process</param>
        public void FireOnProcessStarted(System.Diagnostics.Process process)
        {
            foreach (var ext in Extensions)
            {
                try
                {
                    ext.Value.OnProcessStarted(process);
                }
                catch (ExtensionException ex)
                {
                    Log.Error("onProcessStarted() handler failed for " + ext.Value.DisplayName, ex);
                }
            }
        }

        /// <summary>
        /// Handler, which is being invoked once the child process is terminated.
        /// </summary>
        /// <param name="process">Process</param>
        public void FireOnProcessTerminated(System.Diagnostics.Process process)
        {
            foreach (var ext in Extensions)
            {
                try
                {
                    ext.Value.OnProcessTerminated(process);
                }
                catch (ExtensionException ex)
                {
                    Log.Error("onProcessTerminated() handler failed for " + ext.Value.DisplayName, ex);
                }
            }
        }

        // TODO: Implement loading of external extensions. Current version supports internal hack
        #region Extension load management

        /// Loads extensions according to the configuration file.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <exception cref="Exception">Loading failure</exception>
        public void LoadExtensions()
        {
            var extensionIds = ServiceDescriptor.ExtensionIds;
            foreach (string extensionId in extensionIds)
            {
                LoadExtension(extensionId);
            }
        }

        /// <summary>
        /// Loads extensions from the configuration file
        /// </summary>
        /// <param name="id">Extension ID</param>
        /// <param name="logger">Logger</param>
        /// <exception cref="Exception">Loading failure</exception>
        private void LoadExtension(string id)
        {
            if (Extensions.ContainsKey(id))
            {
                throw new ExtensionException(id, "Extension has been already loaded");
            }

            XmlNode? extensionsConfig = ServiceDescriptor.ExtensionsConfiguration;
            XmlElement? configNode = extensionsConfig is null ? null : extensionsConfig.SelectSingleNode("extension[@id='" + id + "'][1]") as XmlElement;
            if (configNode is null)
            {
                throw new ExtensionException(id, "Cannot get the configuration entry");
            }

            var descriptor = WinSWExtensionDescriptor.FromXml(configNode);
            if (descriptor.Enabled)
            {
                IWinSWExtension extension = CreateExtensionInstance(descriptor.Id, descriptor.ClassName);
                extension.Descriptor = descriptor;
                try
                {
                    extension.Configure(ServiceDescriptor, configNode);
                }
                catch (Exception ex)
                { // Consider any unexpected exception as fatal
                    Log.Fatal("Failed to configure the extension " + id, ex);
                    throw ex;
                }

                Extensions.Add(id, extension);
                Log.Info("Extension loaded: " + id);
            }
            else
            {
                Log.Warn("Extension is disabled: " + id);
            }
        }

        private IWinSWExtension CreateExtensionInstance(string id, string className)
        {
            object created;

            try
            {
                Type? t = Type.GetType(className);
                if (t is null)
                {
                    throw new ExtensionException(id, "Class " + className + " does not exist");
                }

                created = Activator.CreateInstance(t)!;
            }
            catch (Exception ex)
            {
                throw new ExtensionException(id, "Cannot load the class by name: " + className, ex);
            }

            if (!(created is IWinSWExtension extension))
            {
                throw new ExtensionException(id, "The loaded class is not a WinSW extension: " + className + ". Type is " + created.GetType());
            }

            return extension;
        }

        #endregion
    }
}
