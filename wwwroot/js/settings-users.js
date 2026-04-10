(function(global){
  async function dbLogin(id,pw){
    return new Promise(resolve=>{
      let done=false;
      const handler=(msg)=>{
        if(msg?.type!=='authResult') return;
        done=true;
        window.removeEventListener('csharp-auth',listener);
        resolve(msg.data||{ok:false});
      };
      const listener=(e)=>handler(e.detail);
      window.addEventListener('csharp-auth',listener);
      global.sendToCSharp?.('AUTH_LOGIN','AUTH',JSON.stringify({id,pw}));
      setTimeout(()=>{
        if(done) return;
        window.removeEventListener('csharp-auth',listener);
        resolve({ok:false});
      },2000);
    });
  }

  // doLogin 오버라이드/legacy fallback 제거
  global.SettingsUsers={dbLogin};
})(window);
