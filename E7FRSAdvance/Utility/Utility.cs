using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace E7FRSAdvance.Utility
{
    public class Utility
    {
        public enum Role
        {
            Admin = 1,
            User = 2,
            ZonalAdmin = 4,
            DivisionalAdmin = 5
        }

        public enum Operators
        {
            Equals_Tp = 1,
            Less_Than = 2,
            Less_Than_Equals_To = 3,
            Greater_Than = 4,
            Greater_Than_Equals_To = 5
        }

        public enum PLCLog
        {
            Select = 0,
            FTP = 1,
            HTTP = 2
        }

        public enum GateContectType
        {
            FrontContect = 1,
            BackContect = 2,
            IsDigit = 3
        }

        public enum DataLogEnum
        {
            [Display(Name = "Value")]
            Value = 1,
            [Display(Name = "HttpPostName")]
            HttpPostName = 2
        }

        public enum SeriesOprationType
        {
            IsLH = 1,
            IsRH = 2
        }

        public enum ThickWaveType
        {
            A = 1,
            B = 2,
            Both = 3,
        }

        public enum AdjacentType
        {
            Adjacent_Type = 0,
            Is_LH = 1,
            Is_RH = 2
        }

        public enum Utilities
        {
            AxleCounter = 1,
            Charger = 2,
            Choke = 3,
            EarthFault = 4,
            PointMachine = 5,
            Signal = 6,
            Track = 7,
            Variable = 8
        }

        public enum AssetType
        {
            TRACK = 1,
            SIGNAL = 2,
            POINT_MACHINE = 3,
            AXLE_COUNTER = 4,
            GATE = 5,
            Charger = 6,
            Device_Time = 7,
            TPR_Voltage = 8,
            Signal_Current = 9,
            Earth_Fault = 13,
            Bus_Bar = 14,
            Relays_GATE_GJ_2 = 15,
            Relay = 20,
            TRD_MAST_CURRENT = 21,
            Relay_Room_Bus_Bar = 22,
            UFSBI = 23,
            BRIDGE = 25,
            BHMS = 28,
            Distant_Signal = 30,
            ELD = 31,
            IPS = 34,
            Temperature = 37,
            Humidity = 38,
            Equipment_Room = 39
        }

        public enum DLAssetType
        {
            Door = 1,
            Gate = 2,
            IPS = 3,
            Point = 4,
            Track = 5,
            Signal = 6,
            ELD = 7,
            UFSBI = 8,
            Slot = 9,
            Power_Supply = 10,
            Axc = 11,

        }
        public enum VibrationType
        {
            A_End = 1,
            B_End = 2,
            Half_Point_Machine = 3
        }

        public enum IPSType
        {
            [Display(Name = "24V DC")]
            DC24 = 1,
            [Display(Name = "60V DC")]
            DC60 = 2,
            [Display(Name = "110V DC")]
            DC110 = 3,
            [Display(Name = "110V AC")]
            AC110 = 4
        }

        public enum AppKeyEnum
        {
            TrainRunningMomentRemark = 1,
            GluedLogDataRemark = 2,
            PointMachineDataTableRemark = 3
        }

        public enum SummaryType
        {
            Glued = 1,
            TrainMoment = 2,
            Leakage = 3,
            Energization = 4,
            BatteryDefective = 5,
            Signature = 6,
            EarthFault = 7,
            AxleCounter = 8
        }

        public enum PMPrediction
        {
            Select = 0,
            Modrate_High = 1,
            Insufficient_Data = 2,
            Ideal = 3
        }
        public enum PMIsThickWayPrediction
        {
            Select = 0,
            Ideal = 1,
            Insufficient_Data = 2,
            Modrate_High = 3,
        }

        public enum OperationType
        {
            Include = 1,
            Exclude = 2,
            IncludeApp = 3,
            ExcludeApp = 4,
        }

        public enum ClasificationTypes
        {
            Image = 1,
            Array = 2
        }

        public enum ClusterStatus
        {
            Helthy = 1,
            Alert = 2
        }

        public enum PointMachineType
        {
            TWS = 1,
            IRS = 2
        }

        public enum ADCType
        {
            [Display(Name = "1056")]
            Special_1056 = 1,
            [Display(Name = "1505")]
            Normal = 2,
            [Display(Name = "1507")]
            Special_1507 = 3,
        }

        public enum DisplacementType
        {
            Select = 0,
            Normal_Open = 1,
            Normal_Close = 2
        }

        public enum OperatorType
        {
            Equals_To = 1,
            Less_than = 2,
            Less_than_Equals_To = 3,
            Greater_than = 4,
            Greater_than_Equals_To = 5
        }

        public enum PDFGenerationType
        {
            None = 1,
            PDF = 2,
            Exception = 3,
        }

        public enum WatchListCreationType
        {
            System = 1,
            User = 2
        }

        public enum JobCardStatus
        {
            Lock = 1,
            Unlock = 2,
            OverallSet = 3
        }
        public enum CommissioningDocument
        {
            [Display(Name = "Network Details")]
            NetworkDetails = 1,
            [Display(Name = "Bit Chart")]
            BitChart = 2,
            [Display(Name = "Cluster Validation")]
            ClusterValidation = 3,
            [Display(Name = "General Circuit Diagram")]
            GeneralCircuitDiagram = 4,
            [Display(Name = "Joint Calibration Report")]
            JointCalibrationReport = 5,
            [Display(Name = "Material Positioning")]
            MaterialPositioning = 6,
            [Display(Name = "Site Survey")]
            SiteSurvey = 7,
            [Display(Name = "Lab Test Report")]
            LabTestReport = 8,
            [Display(Name = "RDPMS Manual")]
            RDPMSManual = 9,
            [Display(Name = "RDPMS Commissioning List")]
            RDPMSCommissioningList = 10,
            [Display(Name = "Pannel Test Report")]
            PannelTestReport = 11,
            [Display(Name = "Circuit Drawing")]
            CircuitDrawing = 12,
            [Display(Name = "RDPMS Booklet")]
            RDPMSBooklet = 13,
            [Display(Name = "Energy7 RDPMS Architecture")]
            Energy7RDPMSArchitecture = 14
        }

        public enum SiteSurveyAssetType
        {
            TRACK = 1,
            SIGNAL = 2,
            POINT_MACHINE = 3,
            AXLE_COUNTER = 4,
            // GATE = 5,
            //Charger = 6,
            //Device_Time = 7,
            //TPR_Voltage = 8,
            //Signal_Current = 9,
            //Earth_Fault = 13,
            Bus_Bar = 14,
            //Relays_GATE_GJ_2 = 15,
            //Relay = 20,
            //TRD_MAST_CURRENT = 21,
            //Relay_Room_Bus_Bar = 22,
            UFSBI = 23,
            //BRIDGE = 25,
            BHMS = 28
            //Distant_Signal = 30,
            //ELD = 31
        }

        public enum SurveyClusterStatus
        {
            Trenching = 1,
            Fitting = 2,
            Wiring = 3,
            Termination = 4,
            A10 = 5,
            Isolation = 6,
            Link_Test = 7,
            Validation = 8,
            Pannel_Testing_Track = 9,
            Pannel_Testing_Signal = 10,
            Point_Machine_Test = 11,
            Joint_Calibration = 12,
        }

        public enum JobCardStatusType
        {
            NotStart = 0,
            Start = 1,
            Pause = 2,
            PauseStart = 3,
            Stop = 4
        }

        public enum JobCardOverallTargetType
        {
            LinkIssue = 1,
            WatchLists = 2,
            FRSWatchList = 3,
            Manually = 4
        }

        public enum Datalogger
        {
            Front = 1,
            Back = 2
        }

        public enum PointAlertAlias
        {
            [Display(Name = "High Current")]
            High_Current = 1,

            [Display(Name = "Low Current")]
            Low_Current = 2,

            [Display(Name = "Low Voltage")]
            Low_Voltage = 3,

            [Display(Name = "High Voltage")]
            High_Voltage = 4,

            [Display(Name = "High Time")]
            High_Time = 5,
        }

        public enum AlertType
        {
            Predictive = 1,
            Failure = 2
        }

        public enum AcknowledgemenStatus
        {
            Not_Given = 0,
            Pending = 1,
            True = 2,
            False = 3,
            Maintenace = 4,
            PT = 5
        }

        public enum PMScale
        {
            Scale_100 = 1,
            Scale_20 = 2,
        }

        public enum AlertStatus
        {
            InActive = 1,
            Active = 2
        }

        public enum FRSAssetType
        {
            TRACK = 1,
            SIGNAL = 2,
            POINT_MACHINE = 3,
            AXLE_COUNTER = 4,
            GATE = 5,
            IPS = 34
        }

        public enum WorksheetMaterial
        {
            E7 = 1,
            Railway = 2
        }

        public enum ConditionType
        {
            Set = 1,
            Reset = 2
        }

        public enum ModemType
        {
            Single = 1,
            Dual = 2
        }
        public enum SingleModamType
        {
            Complate = 1,
            Partial = 2
        }

        public enum BHMSType
        {
            Cumulative = 1,
            Direct = 2
        }

        public enum DataloggerVersion
        {
            [Display(Name = "Ver1(Request)")]
            Ver1 = 1,
            [Display(Name = "Ver2(Broadcast)")]
            Ver2 = 2
        }

        public enum FRSAlertIssue
        {
            [Display(Name = "H")]
            Hardware = 1,
            [Display(Name = "W")]
            Wattmon = 2,
            [Display(Name = "C")]
            Calibration = 3,
            [Display(Name = "LI")]
            Logic = 4,
            [Display(Name = "DL")]
            DataLogger = 5,
            [Display(Name = "FT")]
            FamilyOfTrack = 6,
            [Display(Name = "DO")]
            Doubt = 7,
            [Display(Name = "VI")]
            Validation = 8
        }

        public enum DispatchType
        {
            Train = 1,
            Bus = 2,
            Other = 3
        }

        public enum FRSAlertStatus
        {
            Active = 2,
            InActive = 1
        }

        public enum PowerSupplyType
        {
            [Display(Name = "24 V")]
            V24 = 1,
            [Display(Name = "110 V")]
            V110 = 2,
            [Display(Name = "Converter 110 to 24 V")]
            ConverterV110V24 = 3
        }

        public enum WatchIssue
        {
            [Display(Name = "Flus Blown")]
            FlusBlown,

            [Display(Name = "(0-3)Sensor faulty")]
            SensorFaulty_0_3,

            [Display(Name = "(0-10)Sensor faulty")]
            SensorFaulty_0_10,

            [Display(Name = "(0-600)Sensor faulty")]
            SensorFaulty_0_600,

            [Display(Name = "A10 faulty")]
            A10Faulty,

            [Display(Name = "mint card led faulty")]
            MintCardLedFaulty,

            [Display(Name = "current card faulty")]
            CurrentCardFaulty,

            [Display(Name = "power supply card faulty")]
            PowerSupplyCardFaulty,

            [Display(Name = "hub port faulty")]
            HubPortFaulty,

            [Display(Name = "converter port faulty")]
            ConverterPortFaulty,

            [Display(Name = "internet issue")]
            InternetIssue,

            [Display(Name = "anteena issue")]
            AntennaIssue,

            [Display(Name = "spare used by railway")]
            SpareUsedByRailway,

            [Display(Name = "alteration done by railway")]
            AlterationDoneByRailway,

            [Display(Name = "calibration issue")]
            CalibrationIssue,

            [Display(Name = "DC isolation faulty")]
            DCIsolationFaulty,

            [Display(Name = "AC isolation faulty")]
            ACIsolationFaulty,

            [Display(Name = "No Issue")]
            NoIssue,

            [Display(Name = "Other")]
            Other
        }

        public enum Annexure
        {
            Basic = 1,
            FRS_25 = 2,
            FRS_25Advanced = 3
        }

        public enum ValueType
        {
            Current = 1,
            Voltage = 2
        }

        public enum ModemVersion
        {
            Version1 = 1,
            Version2 = 2,
            Version3 = 3
        }

        public enum CurrentSensorType
        {
            [Display(Name = "0_3 A")]
            a03 = 1,
            [Display(Name = "0-10 (A)")]
            a10 = 2,
            [Display(Name = "0-600 mA(AC)")]
            ma600 = 3,
            [Display(Name = "0-10 A(DC)")]
            a10DC = 4,

        }

        public enum AttributeType
        {
            [Display(Name = "10")]
            Main = 1,
            [Display(Name = "11")]
            Shunt = 2,
            [Display(Name = "12")]
            Calling = 3,
            [Display(Name = "13")]
            Route = 4
        }

        public enum VoltageSensorType
        {
            [Display(Name = "0-150V (AC)")]
            AC0_150 = 1,
            [Display(Name = "0-10 V (DC)")]
            DC0_10 = 2,
            [Display(Name = "0-50 V (DC)")]
            DC0_50 = 3,
            [Display(Name = "0-150 V (DC)")]
            DC0_150 = 4,
            [Display(Name = "0-100 V")]
            V0_100 = 5,
            [Display(Name = "0-1 V")]
            V0_1 = 6,
            [Display(Name = "0-1 V(High FREQUENCY)")]
            V0_1HF = 7,

        }

        public enum PointMachineEventUpdateType
        {
            [Display(Name = "CS")]
            Cluster = 1,
            [Display(Name = "MS")]
            MQTT = 2,
            [Display(Name = "FS")]
            FTP = 3
        }

        public enum UserClass
        {
            ESM = 1,
            SSECSI = 2,
            SRDSTECONTROL = 3,
            ENERGY7CONTROL = 4,
            Supervisor = 9,
            ASTEDSTE = 10,
            JE = 11,
            Guest = 14,
            OtherOfficer = 15
        }
        public enum UserClassType
        {
            StationLevelUser = 1,
            DivisionLevelUser = 2,
            HeadquarterLevelUser = 3,
            GuestUser = 4,
            VendorUser = 5
        }
    }
}