(function(global){
  async function dbLogin(id,pw,reqId){
    return new Promise(resolve=>{
      let done=false;
      const handler=(msg)=>{
        if(msg?.type!=='authResult') return;
        const data=msg.data||{};
        if(reqId && data.reqId && String(data.reqId)!==String(reqId)) return;
        done=true;
        window.removeEventListener('csharp-auth',listener);
        resolve(data);
      };
      const listener=(e)=>handler(e.detail);
      window.addEventListener('csharp-auth',listener);
      global.sendToCSharp?.('AUTH_LOGIN','AUTH',JSON.stringify({id,pw,reqId}));
      setTimeout(()=>{
        if(done) return;
        window.removeEventListener('csharp-auth',listener);
        resolve({ok:false,message:'AUTH_TIMEOUT'});
      },2500);
    });
  }

  // DB 전용 로그인 오버라이드 (legacy fallback 금지)
  global.doLogin=async function(){
    const id=document.getElementById('loginId')?.value?.trim()||'';
    const pw=document.getElementById('loginPw')?.value?.trim()||'';
    if(!id || !pw){
      global._finalizeLoginFail?.();
      return;
    }

    if(!(global.chrome?.webview)){
      global._finalizeLoginFail?.();
      return;
    }

    const reqId = Date.now().toString(36) + Math.random().toString(36).slice(2,7);
    const res = await dbLogin(id,pw,reqId);
    if(res.ok){
      const role = (res.role === 'worker') ? 'worker' : 'admin';
      if (typeof global._finalizeLoginSuccess === 'function') {
        global._finalizeLoginSuccess(role, res.userId || id);
      } else {
        global.userRole = role;
        global.currentUserId = res.userId || id;
        global.afterLogin?.();
      }
      global.CommBridge?.send('SAVE_AUDIT_LOG','AUTH',{userId:id,action:'LOGIN',target:'UI',payload:{mode:'db_only'}});
      return;
    }

    global._finalizeLoginFail?.();
  };

  global.SettingsUsers={dbLogin};
})(window);
