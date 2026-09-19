#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>

namespace {
constexpr UINT kInit=WM_APP+0x391,kAction=WM_APP+0x392;
constexpr wchar_t kReady[]=L"HotAMcp.GameBridge.v1";
HMODULE module;
struct GameMessage {int command,subtype,item,flags,x,y;void* parameter;void* dialog;};
static_assert(sizeof(GameMessage)==32);
template<typename T>T Read(uintptr_t p){return *reinterpret_cast<T*>(p);}

uintptr_t pendingDialog;
uintptr_t originalTable;
uintptr_t dialogTable[15];
GameMessage pendingMessage;
uintptr_t pendingButton,buttonOriginal,buttonTable[13];
GameMessage buttonMessage;
int __fastcall DeliverButtonCommand(void* self,void*,GameMessage* message){
    if(reinterpret_cast<uintptr_t>(self)!=pendingButton)return 0;
    *reinterpret_cast<uintptr_t*>(self)=buttonOriginal;
    pendingButton=0;
    *message=buttonMessage;
    // ProcessItems stops here; the modal loop delivers this result to its
    // actual callback, including HD's custom menu callback.
    return 2;
}
bool pendingButtonExit;
int __fastcall DeliverDialogCommand(void* self,void*,GameMessage* message){
    if(reinterpret_cast<uintptr_t>(self)!=pendingDialog)return 0;
    *reinterpret_cast<uintptr_t*>(self)=originalTable;
    pendingDialog=0;
    *message=pendingMessage;
    if(pendingButtonExit){
        pendingButtonExit=false;
        // Commit the ordinary close-button result consumed by ShowAndRun.
        *reinterpret_cast<int*>(Read<uintptr_t>(0x6992d0)+0x38)=message->item;
        message->subtype=10;
        return 2;
    }
    auto handler=reinterpret_cast<int(__thiscall*)(void*,GameMessage*)>(Read<uintptr_t>(originalTable+0x24));
    return handler(self,message);
}
uintptr_t pendingManager,managerOriginal,managerTable[3];
GameMessage managerMessage;
int __fastcall DeliverManagerCommand(void* self,void*,GameMessage* message){
    if(reinterpret_cast<uintptr_t>(self)!=pendingManager)return 0;
    uintptr_t original=managerOriginal;
    *reinterpret_cast<uintptr_t*>(self)=original;pendingManager=0;
    *message=managerMessage;
    auto handler=reinterpret_cast<int(__thiscall*)(void*,GameMessage*)>(Read<uintptr_t>(original+8));
    return handler(self,message);
}
// Closed command vocabulary. Never accepts arbitrary addresses, function pointers or game-state writes.
bool Dispatch(unsigned operation,int player,int argument) {
    if(player<0||player>7)return false;
    uintptr_t main=Read<uintptr_t>(0x699538);
    if(operation!=20&&operation!=21&&operation!=22&&operation!=23&&operation!=24&&operation!=25&&(Read<int>(0x69ccf4)!=player||Read<uintptr_t>(0x69ccfc)!=main+0x20ad0+player*0x168))return false;
    uintptr_t manager=Read<uintptr_t>(0x6992d0),dialog=Read<uintptr_t>(manager+0x54);
    uintptr_t expected=(operation==1||operation==3||operation==31)?0x63a5e4:operation==2?0x642478:(operation==4||operation==9)?0x64373c:(operation==5||operation==10)?0x6437b0:(operation==6||operation==7)?0x643954:operation==20?0x63ff60:operation==21?0x63e6d8:(operation==22||operation==23||operation==24||operation==25)?0x641cbc:(operation==26||operation==29||operation==30)?0x63db40:0;
    if(!expected||Read<uintptr_t>(dialog)!=expected)return false;
    if(operation==31){
        int x=argument&255,y=(argument>>8)&255,z=(argument>>16)&1;
        int size=Read<int>(0x6783c8);
        if(size<36||size>252||x>=size||y>=size||z>Read<uint8_t>(main+0x1fc48))return false;
        uintptr_t vision=Read<uintptr_t>(0x698a48);
        if(!(Read<uint8_t>(vision+((z*size+y)*size+x)*2)&(1<<player)))return false;
        uintptr_t adventure=Read<uintptr_t>(0x6992b8);
        if(Read<uintptr_t>(adventure)!=0x63a678||Read<int>(adventure+0x34)!=1)return false;
        auto plan=reinterpret_cast<void(__thiscall*)(void*,uint32_t)>(0x419400);
        uint32_t packed=static_cast<uint32_t>(x|(y<<16)|(z<<26));
        plan(reinterpret_cast<void*>(adventure),packed);
        // The normal map-selection handler owns hero destination and path changes.
        // Only its UI target context is supplied here; no hero/world fields are written.
        uintptr_t originalTarget=Read<uint32_t>(adventure+0xe8);
        *reinterpret_cast<uint32_t*>(adventure+0xe8)=packed;
        GameMessage select{8,0,0,0,0,0,nullptr,reinterpret_cast<void*>(dialog)};
        uint32_t outputPosition=0;int outputResult=0;
        auto choose=reinterpret_cast<void(__thiscall*)(void*,GameMessage*,uint32_t*,int*)>(0x40a530);
        choose(reinterpret_cast<void*>(adventure),&select,&outputPosition,&outputResult);
        *reinterpret_cast<uint32_t*>(adventure+0xe8)=static_cast<uint32_t>(originalTarget);
        return true;
    }
    if(operation==3){
        uintptr_t owner=Read<uintptr_t>(0x69ccfc);
        if(Read<uint8_t>(owner+0x3e)<1)return false;
                int townId=argument;if(townId<0||townId>=48)return false;
        bool owned=false;int count=Read<uint8_t>(owner+0x3e);if(count>48)return false;
        for(int i=0;i<count;++i)if(Read<int8_t>(owner+0x40+i)==townId)owned=true;
        if(!owned)return false;
        // Same town UI entry called by the adventure handler at 0x408250.
        if(Read<uint16_t>(0x4081bd)!=0x828b)return false;
        uintptr_t towns=Read<uintptr_t>(main+Read<uint32_t>(0x4081bf));
        uintptr_t town=towns+townId*0x168;
        if(Read<uint8_t>(town)!=townId||Read<int8_t>(town+1)!=player)return false;
        auto openTown=reinterpret_cast<void(__thiscall*)(void*,int)>(0x5be610);
        openTown(reinterpret_cast<void*>(town),0);return true;
    }
    if(operation==4){
        uintptr_t townManager=Read<uintptr_t>(0x69954c);
        if(Read<uintptr_t>(townManager)!=0x643730||Read<int>(townManager+0x34)!=1||Read<uintptr_t>(townManager+0x118)!=dialog)return false;
        uintptr_t town=Read<uintptr_t>(townManager+0x38);
        if(Read<int8_t>(town+1)!=player||(Read<uint32_t>(town+0x150)&0x3c00)==0)return false;
        auto hall=reinterpret_cast<void(__thiscall*)(void*)>(0x5d34d0);
        hall(reinterpret_cast<void*>(townManager));return true;
    }
    if(operation==5&&(argument<0||argument>=18))return false;
    if(operation==24&&!((argument>=281&&argument<=284)||(argument>=287&&argument<=295)||(argument>=307&&argument<=315)||(argument>=326&&argument<=329)||(argument>=331&&argument<=334)||(argument>=3003&&argument<=3005)))return false;
    if(operation==24&&Read<uint8_t>(dialog+0x37e)!=1)return false;
    if(operation==23&&argument!=128&&argument!=129&&argument!=130)return false;
    if(operation==20&&argument!=101&&argument!=102)return false;
    if(operation==21&&argument!=100&&argument!=104)return false;
    int itemId=operation==29?30725:operation==30?30726:operation==25?186:operation==22?188:(operation==20||operation==21||operation==23||operation==24)?argument:operation==1?10:operation==5?600+argument:(operation==9||operation==10)?30720:operation==6?30721:30722;
    uintptr_t first=Read<uintptr_t>(dialog+0x34),last=Read<uintptr_t>(dialog+0x38);
    if(last<first||last-first>8192||(last-first)%4)return false;
    bool found=false;uintptr_t targetButton=0;
    if(operation==26||operation==29||operation==30){
        int count=0; for(uintptr_t item=Read<uintptr_t>(dialog+0x2c);item&&count++<2048;item=Read<uintptr_t>(item+8)){
            if(Read<uintptr_t>(item+4)!=dialog)return false;
            if(Read<uint16_t>(item+0x10)==itemId&&Read<uintptr_t>(item)==0x63bb54&&(Read<uint16_t>(item+0x16)&0x2e)==6&&Read<uint8_t>(item+0x44)==0){found=true;targetButton=item;break;}
        }
    }
    for(uintptr_t p=first;!found&&p<last;p+=4){
        uintptr_t item=Read<uintptr_t>(p);
        if(Read<uint16_t>(item+0x10)!=itemId)continue;
        if(Read<uintptr_t>(item+4)!=dialog||(operation!=5&&Read<uintptr_t>(item)!=0x63bb54&&!(operation==23&&Read<uintptr_t>(item)==0x63bb88))||
            (Read<uint16_t>(item+0x16)&6)!=6)return false;
        if((operation==6||operation==7||operation==10||operation==23||operation==24||operation==25)&&(Read<uint16_t>(item+0x16)&0x28))return false;
        if((operation==6||operation==7||operation==10)&&Read<uint8_t>(item+0x44)!=1)return false;
        found=true;targetButton=item;break;
    }
    if(!found)return false;
    GameMessage message{0x200,operation==5?0xC:0xD,itemId,0,0,0,nullptr,reinterpret_cast<void*>(dialog)};
    if(operation==9){
        uintptr_t townManager=Read<uintptr_t>(0x69954c);
        if(pendingManager||Read<uintptr_t>(townManager)!=0x643730||Read<int>(townManager+0x34)!=1)return false;
        managerOriginal=0x643730;
        for(int i=0;i<3;++i)managerTable[i]=Read<uintptr_t>(managerOriginal+i*4);
        managerTable[2]=reinterpret_cast<uintptr_t>(&DeliverManagerCommand);
        pendingManager=townManager;managerMessage=message;
        *reinterpret_cast<uintptr_t*>(townManager)=reinterpret_cast<uintptr_t>(managerTable);
        return true;
    }
    if(operation==1){
        uintptr_t adventure=Read<uintptr_t>(0x6992b8);
        if(Read<uintptr_t>(adventure)!=0x63a678||Read<int>(adventure+0x34)!=1)return false;
        auto handler=reinterpret_cast<int(__thiscall*)(void*,GameMessage*)>(Read<uintptr_t>(0x63a678+8));
        handler(reinterpret_cast<void*>(adventure),&message);return true;
    }
    if(operation==20||operation==21||operation==22||operation==23||operation==24||operation==25||operation==26||operation==29||operation==30){
        if(pendingButton)return false;
        buttonOriginal=Read<uintptr_t>(targetButton);
        for(int i=0;i<13;++i)buttonTable[i]=Read<uintptr_t>(buttonOriginal+i*4);
        buttonTable[2]=reinterpret_cast<uintptr_t>(&DeliverButtonCommand);
        buttonMessage=message;pendingButton=targetButton;
        *reinterpret_cast<uintptr_t*>(targetButton)=reinterpret_cast<uintptr_t>(buttonTable);
        return true;
    }
    if(pendingDialog)return false;
    // The modal loop consumes callback return values at 0x602C56. Deliver the
    // semantic event there, so its ordinary close/unwind path remains intact.
    originalTable=expected;
    for(int i=0;i<15;++i)dialogTable[i]=Read<uintptr_t>(expected+i*4);
    dialogTable[3]=reinterpret_cast<uintptr_t>(&DeliverDialogCommand);
    pendingButtonExit=operation==6||operation==7||operation==10||operation==26;
    pendingMessage=message;
    pendingDialog=dialog;
    *reinterpret_cast<uintptr_t*>(dialog)=reinterpret_cast<uintptr_t>(dialogTable);
    return true;
}
void SafeDispatch(HWND hwnd,unsigned operation,int player,int argument){
    SetPropW(hwnd,L"HotAMcp.GameBridge.Result",reinterpret_cast<HANDLE>(1));
    __try{
        bool done=Dispatch(operation,player,argument);
        SetPropW(hwnd,L"HotAMcp.GameBridge.Result",reinterpret_cast<HANDLE>(done?2:3));
    }__except(EXCEPTION_EXECUTE_HANDLER){
        SetPropW(hwnd,L"HotAMcp.GameBridge.Result",reinterpret_cast<HANDLE>(4));
    }
}
}
extern "C" __declspec(dllexport) LRESULT CALLBACK GameHook(int code,WPARAM wp,LPARAM lp){
    if(code>=0&&wp==PM_REMOVE){
        auto* message=reinterpret_cast<MSG*>(lp);
        if(message->message==kInit){
            HMODULE pinned=nullptr;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&GameHook),&pinned);
            SetPropW(message->hwnd,kReady,reinterpret_cast<HANDLE>(1));
            message->message=WM_NULL;
        }else if(message->message==kAction&&GetPropW(message->hwnd,kReady)){
            HWND hwnd=message->hwnd;unsigned operation=static_cast<unsigned>(message->wParam);int player=static_cast<int>(message->lParam)&0xff;int argument=static_cast<int>(message->lParam)>>8;
            message->message=WM_NULL;SafeDispatch(hwnd,operation,player,argument);
        }
    }
    return CallNextHookEx(nullptr,code,wp,lp);
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){
    if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;
}
