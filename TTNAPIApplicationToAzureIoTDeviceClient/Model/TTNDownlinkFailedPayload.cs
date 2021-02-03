﻿//---------------------------------------------------------------------------------
// Copyright (c) February 2021, devMobile Software
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
namespace devMobile.TheThingsNetwork.Models
{
   using System.Collections.Generic;
   using System.Runtime.Serialization;

   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;

   public class DownlinkMessage
   {
      public int f_port { get; set; }
      public string frm_payload { get; set; }
      public string priority { get; set; }
      public List<string> correlation_ids { get; set; }
   }

   public class DownlinkFailedError
   {
      [JsonProperty("namespace")]
      public string Namespace { get; set; }
      public string name { get; set; }
      public string message_format { get; set; }
      public int code { get; set; }
   }

   public class DownlinkFailed
   {
      public DownlinkMessage downlink { get; set; }
      [JsonProperty("error")]
      public DownlinkFailedError error { get; set; }
   }

   public class DownlinkFailedPayload
   {
      public EndDeviceIds end_device_ids { get; set; }
      public List<string> correlation_ids { get; set; }
      public DownlinkFailed downlink_failed { get; set; }
   }
}