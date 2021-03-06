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
namespace devMobile.TheThingsNetwork.TTNAPIApplicationToAzureIoTDeviceClient
{
   using System;

   public static class Constants
   {
      public const byte PortNumberMinimum = 1;
      public const byte PortNumberMaximum = 223;

#if DEVICE_FIELDS_MINIMUM
      public static readonly string[] DevicefieldMaskPaths = { "attributes" };
#else
		public static readonly string[] DevicefieldMaskPaths = { "name", "description", "attributes" };
#endif

      public static string AzureCorrelationPrefix = "az:LockToken:";
   }
}
