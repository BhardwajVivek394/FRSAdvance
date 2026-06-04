/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-27 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/


window.example = {
    type: 'bpmn',
    cells: [
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1200,
                'y': 700
            },
            'angle': 0,
            'id': 'dbbdf832-df2c-4816-9faa-5e9041561397',
            'z': 47,
            'embeds': [
                '94ba7035-5863-4e4a-bd56-022f2a992ea6'
            ],
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Request Credit Card Information'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1200,
                'y': 880
            },
            'angle': 0,
            'id': '981f9aa1-a7d2-43be-89d5-d062ed8e59fc',
            'z': 57,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Notify Customer Offer Expired'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1410,
                'y': 910
            },
            'angle': 0,
            'id': '7459b277-f97e-4bac-ac3c-a8cb1e375f7e',
            'z': 65,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'thick'
                },
                'label': {
                    'text': 'Offer Expired'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '981f9aa1-a7d2-43be-89d5-d062ed8e59fc'
            },
            'target': {
                'id': '7459b277-f97e-4bac-ac3c-a8cb1e375f7e'
            },
            'id': '9b3fd097-364e-477b-bc4c-1581ece11f49',
            'z': 66,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1270,
                'y': 780
            },
            'angle': 0,
            'id': '94ba7035-5863-4e4a-bd56-022f2a992ea6',
            'z': 87,
            'parent': 'dbbdf832-df2c-4816-9faa-5e9041561397',
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'timer1'
                },
                'label': {
                    'text': '24 Hours'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '94ba7035-5863-4e4a-bd56-022f2a992ea6',
                'anchor': {
                    'name': 'topLeft',
                    'args': {
                        'dx': '50%',
                        'dy': '65%',
                        'rotate': true
                    }
                }
            },
            'target': {
                'id': '981f9aa1-a7d2-43be-89d5-d062ed8e59fc'
            },
            'id': 'b6d0f35f-76c2-4133-b9f6-531da4f3a024',
            'z': 88,
            'vertices': [
                {
                    'x': 1245,
                    'y': 806
                }
            ],
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1775,
                'y': 870
            },
            'angle': 0,
            'id': '47457fe0-4249-4d65-bba3-0bb010072a35',
            'z': 94,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Notify Failed Booking'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1980,
                'y': 901
            },
            'angle': 0,
            'id': 'c96d3a5d-d162-4d07-8719-8a2a9427d693',
            'z': 96,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'thick'
                },
                'label': {
                    'text': 'Failed Booking'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '47457fe0-4249-4d65-bba3-0bb010072a35'
            },
            'target': {
                'id': 'c96d3a5d-d162-4d07-8719-8a2a9427d693'
            },
            'id': 'c8d12a57-f06d-4c06-b604-bd5cd3f1b337',
            'z': 97,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1780,
                'y': 705
            },
            'angle': 0,
            'id': 'f207b090-56d1-4210-ba2a-7a6180322bd8',
            'z': 98,
            'embeds': [
                'af2b7056-a027-4316-afd9-d3b1013abb6a'
            ],
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'service'
                },
                'label': {
                    'text': 'Charge Credit Card'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1980,
                'y': 555
            },
            'angle': 0,
            'id': '1191c1ab-ae7d-42c8-9b99-625c28744f9f',
            'z': 132,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Notfiy Failed Credit Transaction'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 2185,
                'y': 585
            },
            'angle': 0,
            'id': '605e5d92-3873-43ab-829f-443b4b779b72',
            'z': 134,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'thick'
                },
                'label': {
                    'text': 'Failed Credit Transaction'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '1191c1ab-ae7d-42c8-9b99-625c28744f9f'
            },
            'target': {
                'id': '605e5d92-3873-43ab-829f-443b4b779b72'
            },
            'id': 'ae63c725-c556-4202-af5d-592e4974ae38',
            'z': 135,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 2185,
                'y': 730
            },
            'angle': 0,
            'id': '0cc829fb-59a2-4da3-bb60-218afd3aec8d',
            'z': 136,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'thick'
                },
                'label': {
                    'text': 'Booking Confirmed'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1100,
                'y': 1110
            },
            'angle': 0,
            'id': '7bd847a4-6284-4284-a118-eeae7c3556ff',
            'z': 138,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'message1'
                },
                'label': {
                    'text': 'Cancel Request'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1100,
                'y': 910
            },
            'angle': 0,
            'id': 'bc75b5f7-206a-429b-b7f2-8b63a3699596',
            'z': 140,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'timer1'
                },
                'label': {
                    'text': '24 Hours'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'bc75b5f7-206a-429b-b7f2-8b63a3699596'
            },
            'target': {
                'id': '981f9aa1-a7d2-43be-89d5-d062ed8e59fc'
            },
            'id': 'a3412e36-97ab-4ab2-8f6b-b1243946d639',
            'z': 141,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1100,
                'y': 730
            },
            'angle': 0,
            'id': '243b3f14-0c3c-4ede-be5e-95b5d9b411d6',
            'z': 144,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'message1'
                },
                'label': {
                    'text': 'Offer Approved'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '243b3f14-0c3c-4ede-be5e-95b5d9b411d6'
            },
            'target': {
                'id': 'dbbdf832-df2c-4816-9faa-5e9041561397'
            },
            'id': '2280125d-86ca-46cb-b87c-47a3bb1bb2d2',
            'z': 145,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Gateway',
            'size': {
                'width': 58,
                'height': 58
            },
            'position': {
                'x': 995,
                'y': 901
            },
            'angle': 0,
            'id': '49ee6754-560a-4f00-9463-207526526689',
            'z': 146,
            'attrs': {
                'body': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'event'
                },
                'label': {
                    'text': ''
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '49ee6754-560a-4f00-9463-207526526689'
            },
            'target': {
                'id': '7bd847a4-6284-4284-a118-eeae7c3556ff'
            },
            'id': 'a0175029-1a6a-4799-a54f-7c7032b3b30d',
            'z': 147,
            'vertices': [
                {
                    'x': 1025,
                    'y': 1130
                }
            ],
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '49ee6754-560a-4f00-9463-207526526689'
            },
            'target': {
                'id': '243b3f14-0c3c-4ede-be5e-95b5d9b411d6'
            },
            'id': '12b80046-f882-47ce-b81b-1c28d975c3f9',
            'z': 147,
            'vertices': [
                {
                    'x': 1025,
                    'y': 750
                }
            ],
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '49ee6754-560a-4f00-9463-207526526689'
            },
            'target': {
                'id': 'bc75b5f7-206a-429b-b7f2-8b63a3699596'
            },
            'id': 'ea1a8a2e-df2f-4b8a-9df6-521cde9e8d09',
            'z': 147,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 780,
                'y': 880
            },
            'angle': 0,
            'id': '0ace5a9e-c163-4e76-be11-defe71e629be',
            'z': 148,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Make Flight and Hotel Offer'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '0ace5a9e-c163-4e76-be11-defe71e629be'
            },
            'target': {
                'id': '49ee6754-560a-4f00-9463-207526526689'
            },
            'id': 'c5ac7d38-0643-458a-9362-7f8427713d25',
            'z': 149,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1410,
                'y': 1110
            },
            'angle': 0,
            'id': 'a3395446-72b9-45bd-b972-1e12ba397b68',
            'z': 158,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'thick'
                },
                'label': {
                    'text': 'Request Cancelled'
                }
            }
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1200,
                'y': 1080
            },
            'angle': 0,
            'id': 'af90a349-0054-43d3-ad12-d41e5c541b7a',
            'z': 160,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'service'
                },
                'label': {
                    'text': 'Update Customer Record'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '7bd847a4-6284-4284-a118-eeae7c3556ff'
            },
            'target': {
                'id': 'af90a349-0054-43d3-ad12-d41e5c541b7a'
            },
            'id': '3ea3fe91-e3c2-4d6d-8fca-7eb1b08d403b',
            'z': 161,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'af90a349-0054-43d3-ad12-d41e5c541b7a'
            },
            'target': {
                'id': 'a3395446-72b9-45bd-b972-1e12ba397b68'
            },
            'id': '24ed595b-7ee0-462d-a8d9-5f39e65a2c33',
            'z': 161,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 659,
                'y': 910
            },
            'angle': 0,
            'id': 'a3dcd35d-c57f-491b-92f9-64e6ef6e7360',
            'z': 162,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'message1'
                },
                'label': {
                    'text': 'Receive Travel Request'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'a3dcd35d-c57f-491b-92f9-64e6ef6e7360'
            },
            'target': {
                'id': '0ace5a9e-c163-4e76-be11-defe71e629be'
            },
            'id': '1c22c037-ebc4-4ac3-9d83-d86b22a057d8',
            'z': 163,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.DataObject',
            'size': {
                'width': 48,
                'height': 65
            },
            'position': {
                'x': 655,
                'y': 700
            },
            'angle': 0,
            'id': '3a5b5754-f53d-4e3e-b2a5-22e42c87a9e5',
            'z': 166,
            'attrs': {
                'body': {
                    'fill': '#ffffff'
                },
                'label': {
                    'text': 'Travel Request'
                },
                'dataTypeIcon': {
                    'iconType': 'input'
                }
            }
        },
        {
            'type': 'bpmn2.DataAssociation',
            'source': {
                'id': '3a5b5754-f53d-4e3e-b2a5-22e42c87a9e5'
            },
            'target': {
                'id': '0ace5a9e-c163-4e76-be11-defe71e629be'
            },
            'id': 'f442f1d8-2930-4c4c-ab48-dd61e9eda561',
            'z': 167,
            'vertices': [
                {
                    'x': 840,
                    'y': 735
                }
            ]
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1980,
                'y': 700
            },
            'angle': 0,
            'id': 'cb502dc5-0fd0-4a6e-9495-eb4073eaa005',
            'z': 172,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'icon': {
                    'iconType': 'send'
                },
                'label': {
                    'text': 'Confirm Booking'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'f207b090-56d1-4210-ba2a-7a6180322bd8'
            },
            'target': {
                'id': 'cb502dc5-0fd0-4a6e-9495-eb4073eaa005'
            },
            'id': '5482fac6-1850-4018-bd9e-fe8164bec928',
            'z': 173,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'cb502dc5-0fd0-4a6e-9495-eb4073eaa005'
            },
            'target': {
                'id': '0cc829fb-59a2-4da3-bb60-218afd3aec8d'
            },
            'id': '280da7b6-2918-4d48-ae4c-bdbee4a929e5',
            'z': 173,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.DataObject',
            'size': {
                'width': 48,
                'height': 65
            },
            'position': {
                'x': 2125,
                'y': 835
            },
            'angle': 0,
            'id': 'c3e7dd79-24b9-4010-aaf9-d59a5eaf42c6',
            'z': 175,
            'attrs': {
                'body': {
                    'fill': '#ffffff'
                },
                'label': {
                    'text': 'Itinerary'
                },
                'dataTypeIcon': {
                    'iconType': 'output'
                }
            }
        },
        {
            'type': 'bpmn2.DataAssociation',
            'source': {
                'id': 'cb502dc5-0fd0-4a6e-9495-eb4073eaa005'
            },
            'target': {
                'id': 'c3e7dd79-24b9-4010-aaf9-d59a5eaf42c6'
            },
            'id': '18e0423a-d36b-4ac5-b008-596c532f1da7',
            'z': 176,
            'vertices': [
                {
                    'x': 2060,
                    'y': 860
                }
            ]
        },
        {
            'type': 'bpmn2.Activity',
            'size': {
                'width': 120,
                'height': 100
            },
            'position': {
                'x': 1505,
                'y': 700
            },
            'angle': 0,
            'id': 'c847258e-bb81-4f8d-ad7e-845d9cb1f360',
            'z': 188,
            'embeds': [
                '46c0f118-e206-418d-bcf3-e55f7219b9ed'
            ],
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'label': {
                    'text': 'Make Booking'
                },
                'markers': {
                    'iconTypes': [
                        'sub-process'
                    ]
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'dbbdf832-df2c-4816-9faa-5e9041561397'
            },
            'target': {
                'id': 'c847258e-bb81-4f8d-ad7e-845d9cb1f360'
            },
            'id': 'a83d6a30-a8bd-4dce-8ccd-7fbefab586ff',
            'z': 190,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'c847258e-bb81-4f8d-ad7e-845d9cb1f360'
            },
            'target': {
                'id': 'f207b090-56d1-4210-ba2a-7a6180322bd8'
            },
            'id': 'c105a65e-b421-45b9-a634-063eebdb7b57',
            'z': 190,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1575,
                'y': 780
            },
            'angle': 0,
            'id': '46c0f118-e206-418d-bcf3-e55f7219b9ed',
            'z': 195,
            'parent': 'c847258e-bb81-4f8d-ad7e-845d9cb1f360',
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'error1'
                },
                'label': {
                    'text': ''
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '46c0f118-e206-418d-bcf3-e55f7219b9ed',
                'anchor': {
                    'name': 'topLeft',
                    'args': {
                        'dx': '50%',
                        'dy': '48%',
                        'rotate': true
                    }
                }
            },
            'target': {
                'id': '47457fe0-4249-4d65-bba3-0bb010072a35'
            },
            'id': '7e303f20-b703-47b3-baec-701609c2bb69',
            'z': 196,
            'vertices': [
                {
                    'x': 1595,
                    'y': 915
                }
            ],
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1850,
                'y': 690
            },
            'angle': 0,
            'id': 'af2b7056-a027-4316-afd9-d3b1013abb6a',
            'z': 199,
            'parent': 'f207b090-56d1-4210-ba2a-7a6180322bd8',
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'error1'
                },
                'label': {
                    'text': ''
                }
            }
        },
        {
            'type': 'bpmn2.Event',
            'size': {
                'width': 40,
                'height': 40
            },
            'position': {
                'x': 1905,
                'y': 585
            },
            'angle': 0,
            'id': '31a80e8b-dd33-4323-9436-30d90e082945',
            'z': 203,
            'attrs': {
                'background': {
                    'fill': '#ffffff'
                },
                'border': {
                    'borderType': 'double'
                },
                'icon': {
                    'iconType': 'compensation2'
                },
                'label': {
                    'text': 'Booking'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': 'af2b7056-a027-4316-afd9-d3b1013abb6a'
            },
            'target': {
                'id': '31a80e8b-dd33-4323-9436-30d90e082945'
            },
            'id': '99b8356a-028c-439b-a349-f4b4f01b0099',
            'z': 204,
            'vertices': [
                {
                    'x': 1870,
                    'y': 605
                }
            ],
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        },
        {
            'type': 'bpmn2.Flow',
            'source': {
                'id': '31a80e8b-dd33-4323-9436-30d90e082945'
            },
            'target': {
                'id': '1191c1ab-ae7d-42c8-9b99-625c28744f9f'
            },
            'id': '69a172b4-1f3e-41ca-aa64-e27b2dc088c0',
            'z': 204,
            'attrs': {
                'line': {
                    'flowType': 'sequence'
                }
            }
        }
    ]
};
