(function(global){
  async function dbLogin(id,pw){
    return new Promise(resolve=>{
      const handler=(msg)=>{
        if(msg?.type==='authResult'){ window.removeEventListener('csharp-auth',listener); resolve(msg.data||{ok:false}); }
      };
      const listener=(e)=>handler(e.detail);
      window.addEventListener('csharp-auth',listener);
      global.sendToCSharp?.('AUTH_LOGIN','AUTH',JSON.stringify({id,pw}));
      setTimeout(()=>resolve({ok:false}),2000);
    });
  }

  const legacyDoLogin=global.doLogin;
  global.doLogin=async function(){
    const id=document.getElementById('loginId')?.value?.trim()||'';
    const pw=document.getElementById('loginPw')?.value?.trim()||'';
    const res=await dbLogin(id,pw);
    if(res.ok){
      global.userRole=res.role||'worker';
      global.currentUserId=id;
      global.afterLogin?.();
      global.CommBridge?.send('SAVE_AUDIT_LOG','AUTH',{userId:id,action:'LOGIN',target:'UI',payload:{mode:'db'}});
      return;
    }
    return legacyDoLogin?.apply(this,arguments);
  };

  global.SettingsUsers={dbLogin};
})(window);
