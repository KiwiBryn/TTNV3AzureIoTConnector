﻿//---------------------------------------------------------------------------------
// Copyright (c) December 2020, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
   using System;

   public static class Constants
   {
      public const byte PortNumberMinimum = 1;
      public const byte PortNumberMaximum = 223;

      public static readonly string[] DevicefieldMaskPaths = { "attributes" };

      public static readonly string DeviceAzureIntegrationProperty = "azureintegration";

      public static readonly string DeviceDtdlModelIdProperty = "dtdlmodelid"; // this has to be lower case as TTI only supports lower case attribute names

      public static readonly string AzureDpsGlobalDeviceEndpoint = "global.azure-devices-provisioning.net";
   }
}
